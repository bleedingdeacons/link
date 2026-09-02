# Link

[![CI](https://github.com/bleedingdeacons/link/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/link/actions/workflows/ci.yml)
[![Semgrep](https://github.com/bleedingdeacons/link/actions/workflows/semgrep.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/link/actions/workflows/semgrep.yml)
[![Coverage Status](https://coveralls.io/repos/github/bleedingdeacons/link/badge.svg?branch=main)](https://coveralls.io/github/bleedingdeacons/link?branch=main)

A .NET MAUI messaging app for an AA intergroup. The server side is the
[Fellowship](https://github.com/bleedingdeacons/fellowship) WordPress
plugin, which holds the messages and pushes them.

Android today. iOS is designed for and not built — see **What is not
done**.

## What it does

A member installs Link, signs in with the email address their intergroup
already holds, and can message individual members and — where the
intergroup allows it — whole committees. Messages arrive as push
notifications, and the handset keeps its own clearable copy.

The two things worth knowing before reading any code:

**Recipients are picked, never typed.** The directory Fellowship sends is
anonymous names and opaque member ids. No email addresses, no telephone
numbers. Link sends ids back and the server does the addressing — so a
stolen handset yields a list of first names rather than the intergroup's
contact database, and a message cannot be addressed to a non-member by
inventing an address.

**Nothing readable crosses Google.** The notification tray shows the
sender's name and "New message". That is all it ever shows, because that
is all the app puts there.

## The encryption, stated precisely

On first attach Link generates an RSA-2048 keypair in the platform
keystore. The public half goes to Fellowship at enrolment; **the private
half never leaves the handset**. Everything the server sends this device
is sealed to that key.

**Fellowship can still read your messages** — bodies are stored in plain
text server-side, which is what makes committee broadcasts, the message
log and GDPR audit possible. This is not end-to-end encryption and must
not be described as such. What the server does *not* hold is any
handset's private key, so a payload it sealed and pushed yesterday is one
it cannot open today.

### The envelope

Two fields arrive, by push or by poll:

| Field | What |
| --- | --- |
| `k` | A fresh 32-byte AES content key, RSA-OAEP to this device, base64 |
| `p` | The payload, gzipped then AES-256-GCM: 12-byte nonce, 16-byte tag, ciphertext, base64 |

RSA-2048 with OAEP encrypts 214 bytes — less than a paragraph — so the
scheme is hybrid. A fresh content key per message is not caution for its
own sake: GCM fails catastrophically if a key and nonce are ever reused,
and the surest way never to reuse one is never to keep one.

**OAEP uses SHA-1, deliberately.** PHP's `openssl_public_encrypt()` with
`OPENSSL_PKCS1_OAEP_PADDING` uses SHA-1 for the OAEP hash and MGF1, with
no way to request SHA-256 through the API PHP exposes.
`MessagePayloadCipher` matches it with `RSAEncryptionPadding.OaepSHA1`.
SHA-1's collision weakness is a *signature* problem; OAEP relies on
preimage resistance, which is intact. **Changing that one line to
`OaepSHA256` without changing the server produces a message that arrives
on a phone and silently will not open.**

The gzip is not tidiness — sealing and base64'ing the largest payload the
server will accept overflows FCM's 4KB limit without it.

### How that is actually verified

The format is written out twice on purpose, and **each side's test does
the other side's job**. `Sealing.cs` here is Fellowship's PHP sealer
transcribed into C#, and `MessagePayloadCipherTests` opens what it
produces with the shipping cipher. On the server, `MessageSealerTest`
opens an envelope *in PHP* the way this app opens one. Drift on either
side turns the test on the opposite side red.

No fixture is committed, and that is deliberate: a fixture would have to
carry the private key that opens it, and a private key in a public
repository is what push protection blocks and Semgrep flags.

## Push is the fast path, not the reliable one

Every message is stored by Fellowship before any push is attempted, and
Link polls as well as listening. A phone in a tunnel catches up when it
surfaces; a handset whose FCM token silently rotated still gets its
messages; a build with no `google-services.json` — CI's, for instance —
gets all of them, just later.

`MessageService.SyncAsync` asks for everything above the highest id it
holds, so nothing is skipped because a push was dropped, delayed by Doze,
or sent to a token that has moved on. A push and a poll produce the same
envelope and go through the same code, so how a message arrived is not
something the rest of the app has to know.

The message list is drawn from the local history first and synced
afterwards, so the app opens instantly, works offline, and does not blank
itself when a poll fails. A message that arrives by push while the list
is on screen redraws it, which it did not originally — the notification
appeared, the message was stored, and the list went on saying "No
messages yet" until the page was navigated away from and back.

**The token is re-registered at every launch**, and that backstop is not
decoration. Enrolment sends whatever token Firebase has issued by then
and proceeds happily without one, because a handset with no Play Services
must still be able to enrol and poll. Nothing afterwards ever sent it:
`OnNewToken` fires on *rotation*, and a handset whose token simply was
not ready at enrolment never heard from it. That handset stayed poll-only
permanently while looking perfectly healthy from both ends — enrolled in
Fellowship's device list, collecting its messages, just always a poll
interval late. `App.OnStart` now calls `DeviceAuthService.RestoreAsync`,
which sends the current token unconditionally rather than trying to guess
what the server last stored.

## The clearable history

`JsonMessageHistory` keeps one AES-256-GCM encrypted file. One file
rather than a database: it holds a few hundred short messages, it is read
whole, and a database file is awkward to encrypt as a unit.

**Clearing is honest about what it does.** It deletes this handset's
copies. It does not unsend anything, other people still have theirs, and
recent messages come back on the next sync. The confirmation dialog says
so, because "clear history" reads to most people as "delete the
messages".

A file that will not decrypt reads as *no history*, never as an error. A
messaging app that will not start because its cache will not decrypt is a
far worse outcome than one that has lost its cache.

Signing out clears the history too, and that is not optional: signing out
on a handed-on phone has to mean the messages are gone from it.

## When a handset loses its key

Platforms invalidate keystore entries for reasons that have nothing to do
with this app — a restored backup, a changed screen lock, re-enrolled
biometrics. Fellowship cannot see it: from the server a handset with a
lost private key looks perfectly healthy right up until a message it
cannot read.

So Link reports it (`POST /auth/device/key-fault`), the message list
shows a banner, the intergroup's Devices screen shows the handset in red,
and Settings generates a new keypair and presents the public half. The
device row survives, so nobody has to re-enrol.

**Messages already sent stay unreadable, and nobody can undo that.** They
were sealed to content keys wrapped to a public key whose private half no
longer exists — and Fellowship never had that half either, so it cannot
re-seal them. The app clears what it cannot open; the dialog says so
before anybody taps it.

## Project layout

| Project | What |
| --- | --- |
| `…Link` | The MAUI app: views, view models, and everything that touches the platform. Android head only. |
| `…Link.Core` | Plain net10.0. Wire models, the REST client, the cipher, the history, the sync loop. |
| `…Link.Tests` | xUnit v3 over Link.Core. |

**Core exists because a test project cannot reference a MAUI app.** The
app's target framework is `net10.0-android`; a `net10.0` test host has no
compatible framework to resolve against, and there is no such thing as a
`net10.0` head of a MAUI app. So the testable code has to live somewhere
a test project can see it. Hand reached the same arrangement for the same
reason.

The line is not stylistic: the moment a file references `Preferences`,
`SecureStorage`, `FileSystem`, `MainThread` or `WebAuthenticator`, it
stops compiling in Core. That constraint is why the cipher takes a PEM
string, the history takes a path and a key, and the client takes an
`HttpClient` — each of those seams exists so a test can drive it.

`LinkServices` builds the object graph once for **both** the app and the
Android push service, which runs with no MAUI host at all. Registering
fresh instances in `MauiProgram` instead would give the app a second
history over the same file, whose write lock is per-instance.

## What is not done

Stated plainly rather than left to be discovered.

**iOS.** The TFM is not in the csproj — deliberately, because listing it
before the work is done produces a head that compiles and does not work.
`Platforms/iOS/PushRegistrar.ios.cs` is already here and answers "poll
only", and the Apple sign-in path exists in `DeviceAuthService` and in
Fellowship. What is missing: the Firebase iOS SDK, an APNs key on the
Firebase project, the background-fetch entitlement, and Sign in with
Apple wired to the platform sheet.

**Hardware-backed keys.** `DeviceKeyStore` generates the keypair in
managed code and keeps the private half in `SecureStorage`, which on
Android is a preferences file encrypted by a hardware-backed Android
Keystore key. That is meaningfully better than a file in app storage and
meaningfully worse than a non-exportable key generated inside the TEE:
here the private key exists as bytes in process memory whenever a message
is opened, and a rooted device could read it.

Moving generation into the Android Keystore proper is the next step —
`RSA/ECB/OAEPWithSHA-1AndMGF1Padding` is supported there, which is
exactly the padding Fellowship uses, so the wire format would not change.
`IDeviceKeyStore` is the seam it goes behind and no caller would need
touching. It was not done in the same pass that built the rest because it
needs Java interop and a real device to test on, and a documented
compromise beats an undocumented one.

**Threaded conversations.** A reply points at what it answers
(`reply_to`) and the list is flat. A thread model can be derived from
that pointer later; a thread id invented now would have to be guessed for
every existing message.

**Attachments.** None, and none designed. FCM's 4KB data limit means a
photo cannot travel in the envelope at all, so it would need a
fetch-on-open path — a sealed blob the app collects over HTTPS after
being told about it — which does not exist yet.

## What it says about itself

Serilog, to a rolling daily file under the app's data directory
(`logs/link-<date>.log`, seven days kept) and, in Debug, to the IDE.

Link shipped without any of this and the first real fault proved the
cost: the evidence available was a screenshot and Android's own log,
because the app had no account of itself. Several paths swallow
exceptions on purpose — throwing out of `OnMessageReceived` kills the
process Android started to deliver the message, and the message is safe
on the server either way — and "swallowed" had quietly come to mean
"swallowed without trace". The catches still swallow; they no longer do
it silently.

Deliberately **no remote sink**, which is where this differs from Hand.
Hand's failure is a helpline alert that does not ring, and somebody who
is not holding the phone needs to see that. Link's failure is a late
message. Shipping a log-ingestion token in the app to watch for one would
be a credential in every APK for no proportionate benefit, so the file is
pulled with adb when somebody is diagnosing:

```
adb -s <serial> exec-out run-as com.thebleedingdeacons.intergroup.link cat files/logs/link-<date>.log
```

`run-as` works because Debug builds are debuggable; a Release build's
logs are not reachable this way.

The one thing the logger must never do is become a source of failure
itself, so `SetupSerilog` cannot throw: a sink that will not build leaves
Serilog's silent default, and every `Log.*` call downstream becomes a
no-op rather than a null reference — which is exactly the behaviour Link
had before any of this existed.

## Building

```bash
dotnet build TheBleedingDeacons.Intergroup.Link -p:LinkAndroidOnly=true -p:EmbedAssembliesIntoApk=true
```

`EmbedAssembliesIntoApk=true` is not optional for a Debug device build.
Fast Deployment — the .NET Android default in Debug — leaves the managed
assemblies out of the APK, and Hand documents at length how that produces
an APK with incomplete Material resources that dies on launch before any
app code runs. It also fails deceptively on a handset that already has an
embedded APK installed. Do not use it.

Configuration lives in `appsettings.json`, embedded rather than copied so
it cannot be edited on a device to point the app at somebody else's
server — which matters, since the app hands that server an OAuth code.

`Platforms/Android/google-services.json` is git-ignored. Without it the
app builds and works; it just polls instead of being pushed to.

```bash
dotnet test TheBleedingDeacons.Intergroup.Link.Tests
```

No workload needed — the test project and Link.Core are plain net10.0.

## The four places `link://auth` is written

The custom scheme has to agree in four places, and a mismatch shows up as
a browser tab that opens and never comes back, with nothing in any log to
say why:

1. `appsettings.json` → `CallbackUrl`
2. `Platforms/Android/WebAuthenticatorCallbackActivity.cs` → the intent filter
3. Fellowship's `DeviceRedirectValidator` → the allow-list
4. The redirect URI registered with Google

What travels that way is a one-time code, never a token. A custom scheme
can in principle be claimed by another app on the device, which is
precisely why the thing that goes through it is worthless two minutes
later and worthless once used, and why the credential itself is fetched
over TLS from Link's own process afterwards.
