# Link domain model — messages, keys, and what a phone keeps

The living specification is the set of Reqnroll `.feature` files under
[`TheBleedingDeacons.Intergroup.Link.Specs/Features`](../TheBleedingDeacons.Intergroup.Link.Specs/Features).
This document is the narrative overview and glossary behind them: what the
words mean, which decisions are settled, and which are still assumptions.

It was written by reading the code rather than the other way round — the app
came first and the executable specification was reverse-engineered from it —
so where this and the feature files disagree, the feature files are what runs
and this is what needs correcting.

## The core idea

A member signs in with the email address their intergroup already holds, and
can write to individual members and — where the intergroup allows it — whole
committees. Messages arrive sealed to this handset, and the phone keeps its
own copy until its owner says otherwise.

Everything else here is qualification of that sentence: *who* can be written
to (picked, never typed), *how* a message arrives (pushed if it can be,
polled regardless), and *what happens when the key that opens them is gone*.

```
Fellowship stores the message
   ├──(push: sealed, fast, not guaranteed)──┐
   └──(poll: HTTPS, slower, always right)───┴──▶ Open ──▶ history + notification
```

Both routes go through one door. That is where an envelope becomes a message
and where the history remembers it — so a push and the poll behind it produce
one row rather than two.

## Ubiquitous language

- **Message** — one send, as this handset holds it: `(id, uuid, subject,
  body, sender, created_at, reply_to, read_at)`. The **sender** is Unity's
  anonymous name, never a legal one and never an address.
- **Envelope** — a message on the wire: an id in the clear and two sealed
  fields, `k` and `p`. The id is not part of the seal — it is what lets a
  poll page without opening anything.
- **Content key** — a fresh 32-byte AES key per message, wrapped to this
  handset with RSA-OAEP. Never kept, which is what guarantees it is never
  reused.
- **Device keypair** — RSA-2048, generated at enrolment. Fellowship gets the
  public half; the private half never leaves.
- **Device token** — the bearer credential that proves who this handset is,
  issued once and held in platform secure storage.
- **Key fault** — this handset cannot open its messages. Reported, because it
  cannot be inferred: from the server a handset with a lost private key looks
  perfectly healthy right up until a message it cannot read.
- **Recipient** — one thing a message can be addressed to: a member id or a
  committee slug. Never an address.
- **Cleared up to** — the mark a clear leaves behind, so what was cleared
  stays cleared.

## What is sealed, and what is not

| Field | On the wire | Readable by |
| --- | --- | --- |
| `k` | Content key, RSA-OAEP to this device, base64 | This handset only |
| `p` | Body, gzipped then AES-256-GCM (12-byte nonce, 16-byte tag), base64 | This handset only |
| id | In the clear, beside the envelope | Anybody who sees the response |

**OAEP uses SHA-1 and both ends must stay on it.** PHP's
`openssl_public_encrypt()` with `OPENSSL_PKCS1_OAEP_PADDING` offers nothing
else. SHA-1's collision weakness is a *signature* problem; OAEP relies on
preimage resistance, which is intact. Changing that one line to `OaepSHA256`
without changing the server produces a message that arrives on a phone and
silently will not open — which is why 21 scenarios go red when it is.

**Fellowship can still read your messages.** Bodies are stored in plain text
server-side, which is what makes committee broadcasts, the message log and
GDPR audit possible. This is not end-to-end encryption. What the server does
*not* hold is any handset's private key, so a *payload* it sealed yesterday
is one it cannot open today — which protects the push crossing Google, and
nothing in the database, because it can seal the same message afresh to any
key a device presents.

## Push is the fast path, not the reliable one

Every message is stored before any push is attempted, and a sync asks for
everything above the highest id held. So a push that was dropped, delayed by
Doze, or sent to a rotated token costs nothing — the message is collected on
the next pass.

**The poll is strictly exclusive.** Fellowship's query is
`message_id > since`, and a sync asks from the highest id it holds. A message
therefore arrives exactly once, whichever route brought it, and a message
already held is never fetched again.

That has one consequence worth knowing before reading the code.
`JsonMessageHistory.SaveAsync` takes care to keep a local read flag when a
second, unread copy of the same message is saved over it, and reasons that
the poll's copy is where the read flag comes from. **No route reaches that
branch**, because the poll cannot return a message this handset already
holds. It is defensive code with a rationale that no longer holds; the
specification says so rather than pretending to pin it down.

## What a lost key actually costs

Less than five places in these two repositories used to say, and the
correction is worth stating plainly because the wrong version was load
bearing.

`MessageController::inbox` seals on **every fetch**, from a body stored as
plain `TEXT`, to whichever public key the asking device presents. So:

| | Recoverable? |
| --- | --- |
| Messages the server still holds | **Yes** — next sync, once the new key is presented |
| Messages past `retention_days` | No |
| A push sealed and sent before the change | No — nothing re-sends a push |

The consequence for the threat model: the handset keypair protects the
wire and the notification tray. It does not protect the corpus, and the
retention window is the only thing that bounds it.

## Who can be written to

**Recipients are picked, never typed.** The directory is anonymous names and
opaque member ids. Link sends ids and slugs back and the server does the
addressing — so a stolen handset yields a list of first names rather than the
intergroup's contact database, and a message cannot be addressed to a
non-member by inventing an address.

A member's row carries a second line, composed from what is there: home
group, then GSR standing, then intergroup service position, joined by `·`. A
member with none of the three gets no line rather than a blank one holding
the row open. The search reads that line as well as the name — somebody who
wants the Secretary, or the GSR from Tuesday Bristol, is describing a person
the only way they can.

Members and committees share one type and one list. They were held apart
until Fellowship stopped refusing a message addressed to both, on
2026-09-11; once a message can carry four names and two committees at once,
"which list is showing?" stops being a question worth asking the sender.

## Clearing, and the other thing that looks like it

| | What it does | Who calls it |
| --- | --- | --- |
| **Clear** | Deletes this phone's copies and remembers how far they reached | The member, from Settings |
| **Reset** | Deletes everything, the mark included | Sign-out, and nothing else |

**The mark is the whole trick.** A poll asks for everything above the highest
id held, so a store that merely deleted its file went back to asking from
zero and the server refilled it within seconds — in front of a member who had
just been told it was cleared. What stays behind is a single number inside
the same encrypted envelope: no message, no subject, no sender.

**Which is exactly why sign-out must not use it.** The mark belongs to the
member who set it. Left in place for whoever signs in next, it would hand
them an inbox that silently refuses to fetch their own history, with nothing
on screen to explain why.

Clearing never walks the mark backwards, and a message received after a clear
moves it on.

## When a handset loses its key

Platforms invalidate keystore entries for reasons that have nothing to do
with this app — a restored backup, a changed screen lock, re-enrolled
biometrics.

- An envelope that will not open is **kept out of the list**. "Will not open"
  covers every reason at once, because the app can do nothing different about
  any of them.
- The fault is **reported once per sync, not once per message**. A handset
  whose key has gone reports a fault; it does not report fifty.
- **A push reports nothing.** A push handler has no session token, and the
  sync behind it does. Raising "New message" for something the app cannot
  show would be worse than raising nothing.
- **The rest of the page still arrives.** One unopenable envelope does not
  take its neighbours with it.
- **Messages still on the server come back.** Fellowship holds bodies in
  plain text and seals per fetch, so once a new key is presented the next
  sync re-delivers everything inside the retention window. Only what the
  sweep has already taken, and pushes sent before the change, are gone.

## Locked design decisions

- **Nothing readable crosses Google.** The tray shows the sender's name and
  "New message", because that is all the app puts there.
- **HTTPS only, checked before use.** The session token travels as a bearer
  credential and message bodies are readable on the wire. A plaintext base
  would put both in the clear with nothing in the app saying so, and sign-in
  is gated on the check rather than warning after the fact.
- **The REST namespace is appended rather than configured**, so a site cannot
  be pointed at a path that is not Fellowship's.
- **A file that will not decrypt reads as no history, never as an error.** A
  messaging app that will not start because its cache will not decrypt is a
  far worse outcome than one that has lost its cache.
- **A cancelled sign-in is not a failure** and shows nothing. A refused one
  carries the server's own words, because "that address does not match a
  member record" is the only thing the member can act on.
- **The four states of the push indicator**, in the order they can fail:
  transport, then permission, then registration. Amber is for a phone the
  sync is still carrying; red is kept for the state where a message can
  arrive and the member is never told.

## Assumptions still open to change

- The history is bounded by nothing but the member's own clearing. Hand caps
  its equivalent at 500 rows; here a few hundred short messages was judged
  small enough not to need a policy, which is a judgement rather than a
  measurement.
- Read state is never re-synced downwards for a message already held — see
  the exclusive poll above. Nobody has yet been troubled by it.
- The arrival sound is a single on/off. A phone that should be quiet in
  meetings and loud otherwise has no way to say so.
- Threads are derived from `reply_to` or not at all. A thread id invented now
  would have to be guessed for every existing message.
