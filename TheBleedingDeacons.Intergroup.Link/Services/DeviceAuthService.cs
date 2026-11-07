using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Signing in, and the key exchange that happens at the same moment.
///
/// <para><b>Two things happen here that must not come apart.</b> The
/// member proves who they are, and this handset generates the keypair
/// everything sent to it will be sealed to. Fellowship refuses an
/// enrolment with no usable public key, so there is no state in which a
/// device is enrolled but unreachable — which is the whole reason the key
/// is made here rather than lazily on the first message.</para>
///
/// <para>Lives in the app rather than Link.Core because it reaches for
/// <c>WebAuthenticator</c> and <c>DeviceInfo</c>, both of which come from
/// the MAUI workload.</para>
/// </summary>
public sealed class DeviceAuthService
{
	private readonly IFellowshipClient _client;
	private readonly IDeviceKeyStore _keys;
	private readonly ISessionStore _sessions;
	private readonly IPushRegistrar _push;
	private readonly IMessageHistory _history;
	private readonly IAppleSignIn _apple;
	private readonly INativeGoogleSignIn _nativeGoogle;
	private readonly FingerprintSignIn _fingerprint;

	private const string GoogleUnavailable =
		"Google sign-in isn't available on this phone. Please sign in with your password.";

	public DeviceAuthService(
		IFellowshipClient client,
		IDeviceKeyStore keys,
		ISessionStore sessions,
		IPushRegistrar push,
		IAppleSignIn apple,
		INativeGoogleSignIn nativeGoogle,
		IMessageHistory history,
		FingerprintSignIn fingerprint)
	{
		_fingerprint = fingerprint;
		_apple = apple;
		_nativeGoogle = nativeGoogle;
		_client = client;
		_keys = keys;
		_sessions = sessions;
		_push = push;
		_history = history;
	}

	/// <summary>
	/// Whether Fellowship has accepted a push token for this handset since
	/// the process started.
	///
	/// <para><b>Not "do we hold a token".</b> Those two disagree in
	/// exactly the case the settings indicator exists to catch: a phone
	/// with a perfectly good FCM token that the intergroup was never told
	/// about, because the one call that would have told it failed while
	/// the phone was out of signal. Such a phone looks push-capable from
	/// the inside and is never pushed to.</para>
	///
	/// <para><b>In memory, and reset by a restart, deliberately.</b> It
	/// means "as of this launch, the intergroup was told" — which is the
	/// claim the indicator can actually stand behind. Persisting it would
	/// make it a memory of something that was true on a different day, and
	/// a device revoked from the admin Devices screen would still read as
	/// registered here.</para>
	/// </summary>
	public bool PushRegistered { get; private set; }

	/// <summary>
	/// Re-send this handset's push token, if it has one and is signed in.
	///
	/// <para>Called at every launch. See <see cref="App.OnStart"/> for why
	/// a handset that enrolled before Firebase issued a token would
	/// otherwise stay poll-only permanently.</para>
	///
	/// <para>Sent unconditionally rather than only when it has changed:
	/// this side cannot know what the server last stored, the call is one
	/// request at startup, and the failure it guards against is silent and
	/// permanent. Answers whether anything was sent, for the log.</para>
	/// </summary>
	public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return false;
		}

		var pushToken = await _push.CurrentTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(pushToken))
		{
			// Normal on a handset with no Play Services, and normal on one
			// where Firebase has simply not finished yet. Either way it
			// polls, and the next launch tries again.
			Log.Information("No push token available at launch; this handset will poll");
			return false;
		}

		return await RegisterPushTokenAsync(pushToken, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Tell Fellowship a push token, from the launch backstop or from a
	/// rotation the Firebase service heard about.
	/// </summary>
	public async Task<bool> RegisterPushTokenAsync(string pushToken, CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return false;
		}

		var ok = await _client.UpdatePushTokenAsync(session.Token, pushToken, cancellationToken).ConfigureAwait(false);

		// Only ever set true here. A failed attempt leaves an earlier
		// success standing rather than retracting it: the token that was
		// accepted is still the one the intergroup holds, and a phone that
		// dipped out of signal has not stopped being pushed to.
		PushRegistered = PushRegistered || ok;

		if (ok)
		{
			Log.Information("Push token registered with the intergroup");
		}
		else
		{
			Log.Warning("Push token could not be registered; this handset will poll until the next attempt");
		}

		return ok;
	}

	/// <summary>
	/// Sign in with Google: the platform's own picker, then Firebase, then enrolling.
	///
	/// <para>The picker hands back a Firebase ID token, which proves an address once. The
	/// phone then enrols with it, and that identity is deleted.</para>
	/// </summary>
	public async Task<EnrolmentResult> SignInWithGoogleAsync(CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Sign-in", "google");
		try
		{
			return attempt.Finished(await SignInThroughGoogleAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	private async Task<EnrolmentResult> SignInThroughGoogleAsync(CancellationToken cancellationToken)
	{
		if (!_nativeGoogle.IsAvailable)
		{
			return EnrolmentResult.Failed(GoogleUnavailable);
		}

		Log.Debug("Raising the native Google account picker");
		var signIn = await _nativeGoogle.SignInAsync(cancellationToken).ConfigureAwait(false);
		Log.Information("Native Google account picker answered {NativeOutcome}", signIn.Outcome);

		switch (signIn.Outcome)
		{
			case NativeGoogleSignInOutcome.Cancelled:
				return EnrolmentResult.Cancelled(signIn.Detail);
			case NativeGoogleSignInOutcome.Unavailable:
				return EnrolmentResult.Failed(GoogleUnavailable);
		}

		return await EnrolAsync(new EnrolmentRequest
		{
			FirebaseToken = signIn.IdToken,
			PublicKey = await _keys.RegenerateAsync().ConfigureAwait(false),
			Platform = PlatformName(),
			Label = DeviceLabel(),
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Replace this handset's keypair, proving who is holding it with Google.
	///
	/// <para>The keypair can't prove anything: it seals and never authenticates. So a fresh
	/// sign-in is needed, and it has to be the member this phone belongs to. The device, its
	/// token and its place in the intergroup's list all survive, which is the whole reason
	/// this isn't simply enrolling again.</para>
	/// </summary>
	public async Task<RotateKeyResult> ReplaceKeyWithGoogleAsync(CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Key replacement", "google");
		try
		{
			return attempt.Finished(await ReplaceKeyThroughGoogleAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	private async Task<RotateKeyResult> ReplaceKeyThroughGoogleAsync(CancellationToken cancellationToken)
	{
		if (!_nativeGoogle.IsAvailable)
		{
			return RotateKeyResult.Failed(GoogleUnavailable);
		}

		Log.Debug("Raising the native Google account picker");
		var signIn = await _nativeGoogle.SignInAsync(cancellationToken).ConfigureAwait(false);
		Log.Information("Native Google account picker answered {NativeOutcome}", signIn.Outcome);

		return signIn.Outcome switch
		{
			NativeGoogleSignInOutcome.Cancelled => RotateKeyResult.Failed(string.Empty),
			NativeGoogleSignInOutcome.SignedIn => await RotateAsync(
				new RotateKeyRequest { FirebaseToken = signIn.IdToken, PublicKey = string.Empty },
				cancellationToken).ConfigureAwait(false),
			_ => RotateKeyResult.Failed(GoogleUnavailable),
		};
	}

	/// <summary>
	/// Replace the keypair, proving who is holding the handset through
	/// Apple's own sheet. See <see cref="ReplaceKeyWithGoogleAsync"/>
	/// for why a credential is needed at all.
	/// </summary>
	public async Task<RotateKeyResult> ReplaceKeyWithAppleAsync(CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Key replacement", "apple");
		try
		{
			return attempt.Finished(await ReplaceKeyThroughAppleAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	private async Task<RotateKeyResult> ReplaceKeyThroughAppleAsync(CancellationToken cancellationToken)
	{
		if (!_apple.IsAvailable)
		{
			return RotateKeyResult.Failed("This device cannot sign in with Apple.");
		}

		var start = await StartAppleAsync(cancellationToken).ConfigureAwait(false);
		if (start is null || string.IsNullOrEmpty(start.Nonce) || string.IsNullOrEmpty(start.State))
		{
			Log.Warning("Fellowship did not start an Apple sign-in (unreachable, or no Apple set up)");
			return RotateKeyResult.Failed("The intergroup is not set up for Apple sign-in.");
		}

		var idToken = await AppleIdTokenAsync(start.Nonce, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrEmpty(idToken))
		{
			// Dismissed the sheet. Says nothing, exactly as a cancelled
			// sign-in says nothing.
			return RotateKeyResult.Failed(string.Empty);
		}

		return await RotateAsync(
			new RotateKeyRequest { IdToken = idToken, State = start.State, PublicKey = string.Empty },
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Replace the keypair with an email and password. See
	/// <see cref="ReplaceKeyWithGoogleAsync"/>.
	/// </summary>
	public async Task<RotateKeyResult> ReplaceKeyWithPasswordAsync(
		string email,
		string password,
		CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Key replacement", "password");
		try
		{
			if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
			{
				return attempt.Finished(RotateKeyResult.Failed("Please enter your email address and password."));
			}

			// The address, which the redactor turns into a token; never the
			// password.
			Log.Information("Proving the handset's holder with the password for {Email}", email.Trim());

			return attempt.Finished(await RotateAsync(
				new RotateKeyRequest { Email = email.Trim(), Password = password, PublicKey = string.Empty },
				cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	/// <summary>
	/// Generate the new pair and present its public half, with whichever
	/// credential the caller gathered.
	/// </summary>
	private async Task<RotateKeyResult> RotateAsync(RotateKeyRequest request, CancellationToken cancellationToken)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			Log.Warning("A key replacement was asked for on a handset that is not signed in");
			return RotateKeyResult.Failed("This device is not signed in.");
		}

		// Generated here rather than by the caller, so the old pair is not
		// thrown away until a credential has actually been gathered — a
		// member who backs out of the browser tab keeps whatever they had.
		var publicKey = await _keys.RegenerateAsync().ConfigureAwait(false);
		Log.Information("New keypair generated; presenting it for device {DeviceId}", session.DeviceId);

		return await _client.RotateKeyAsync(
			session.Token, request with { PublicKey = publicKey }, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Sign in with Apple.
	///
	/// <para>The platform sheet hands the app a signed ID token directly,
	/// so the state issued here goes back with it and Fellowship looks the
	/// nonce up rather than trusting the app to repeat it. The sheet
	/// itself is iOS-only and is not wired up yet — see README.md, "What
	/// is not done" — so this exists to hold the shape rather than to be
	/// called.</para>
	/// </summary>
	public async Task<EnrolmentResult> SignInWithAppleAsync(string idToken, string state, CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Sign-in", "apple");
		try
		{
			return attempt.Finished(await EnrolWithAppleAsync(idToken, state, cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	/// <summary>
	/// Sign in with Apple, end to end: ask Fellowship for a nonce, raise
	/// the platform sheet, enrol with what it hands back.
	///
	/// <para>The counterpart of <see cref="SignInWithGoogleAsync"/>, and
	/// deliberately the same shape despite being a different sheet — the
	/// caller should not have to know which provider it is.</para>
	///
	/// <para><b>Cancelling is not an error.</b> The sheet returning
	/// nothing is overwhelmingly a member changing their mind, and the
	/// screen says nothing rather than accusing them of a failure. That is
	/// why this returns a distinct silent result instead of reusing
	/// <c>EnrolmentResult.Failed</c> with an empty message.</para>
	/// </summary>
	public async Task<EnrolmentResult> SignInWithAppleAsync(CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Sign-in", "apple");
		try
		{
			return attempt.Finished(await SignInThroughAppleAsync(cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	private async Task<EnrolmentResult> EnrolWithAppleAsync(string idToken, string state, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(state))
		{
			Log.Warning("Apple sign-in arrived without {Missing}", string.IsNullOrEmpty(idToken) ? "an ID token" : "a state");
			return EnrolmentResult.Failed("The sign-in did not complete. Please try again.");
		}

		return await EnrolAsync(new EnrolmentRequest
		{
			State = state,
			IdToken = idToken,
			PublicKey = await _keys.RegenerateAsync().ConfigureAwait(false),
			Platform = PlatformName(),
			Label = DeviceLabel(),
		}, cancellationToken).ConfigureAwait(false);
	}

	private async Task<EnrolmentResult> SignInThroughAppleAsync(CancellationToken cancellationToken)
	{
		if (!_apple.IsAvailable)
		{
			return EnrolmentResult.Failed("This device cannot sign in with Apple.");
		}

		var start = await StartAppleAsync(cancellationToken).ConfigureAwait(false);
		if (start is null || string.IsNullOrEmpty(start.Nonce) || string.IsNullOrEmpty(start.State))
		{
			Log.Warning("Fellowship did not start an Apple sign-in (unreachable, or no Apple set up)");
			return EnrolmentResult.Failed("The intergroup is not set up for Apple sign-in.");
		}

		var idToken = await AppleIdTokenAsync(start.Nonce, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrEmpty(idToken))
		{
			return EnrolmentResult.Cancelled("Apple's sign-in sheet was closed.");
		}

		return await EnrolWithAppleAsync(idToken, start.State, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Raise Apple's sheet, and say how it came back — never with what.
	/// </summary>
	private async Task<string?> AppleIdTokenAsync(string nonce, CancellationToken cancellationToken)
	{
		Log.Debug("Raising Apple's sign-in sheet");
		var idToken = await _apple.GetIdTokenAsync(nonce, cancellationToken).ConfigureAwait(false);

		Log.Information(
			"Apple's sheet {AppleOutcome}",
			string.IsNullOrEmpty(idToken) ? "was dismissed" : "returned an ID token");

		return idToken;
	}

	/// <summary>
	/// Sign in with an email and a password.
	///
	/// <para>No provider. This is the only path where the
	/// app handles a secret rather than relaying somebody else's proof of
	/// one, which is why the password is passed straight through to the
	/// request and never kept: there is no "remember me", because a
	/// password stored on a handset to save typing is a password waiting
	/// to be read off it.</para>
	/// </summary>
	public async Task<EnrolmentResult> SignInWithPasswordAsync(
		string email,
		string password,
		CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Sign-in", "password");
		try
		{
			if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
			{
				return attempt.Finished(EnrolmentResult.Failed("Please enter your email address and password."));
			}

			// The address, which the redactor turns into a token: whether
			// it is the same address failing every time is the first
			// question about a refused password. Never the password.
			Log.Information("Password sign-in for {Email}", email.Trim());

			return attempt.Finished(await EnrolAsync(new EnrolmentRequest
			{
				Email = email.Trim(),
				Password = password,
				PublicKey = await _keys.RegenerateAsync().ConfigureAwait(false),
				Platform = PlatformName(),
				Label = DeviceLabel(),
			}, cancellationToken).ConfigureAwait(false));
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	/// <summary>
	/// Ask the intergroup to email a code for setting a password.
	///
	/// <para>Answers only whether the request arrived. Whether a mail was
	/// actually sent is deliberately not knowable from here — that would
	/// say which addresses belong to members — so the screen must say
	/// the same thing either way.</para>
	/// </summary>
	public async Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default)
	{
		var address = (email ?? string.Empty).Trim();

		var reached = await _client.RequestPasswordLinkAsync(address, cancellationToken).ConfigureAwait(false);

		// Only whether it arrived, because that is all there is to know.
		if (reached)
		{
			Log.Information("Password-setting code requested for {Email}", address);
		}
		else
		{
			Log.Warning("Password-setting code request for {Email} did not reach the intergroup", address);
		}

		return reached;
	}

	/// <summary>
	/// Set a password with the code from that email.
	///
	/// <para>Does not sign anybody in. Setting a password and using one
	/// are separate acts, and keeping them separate means a code that
	/// reaches the wrong handset cannot enrol it.</para>
	/// </summary>
	public async Task<PasswordSetResult> SetPasswordAsync(
		string code,
		string password,
		CancellationToken cancellationToken = default)
	{
		var result = await _client.SetPasswordAsync((code ?? string.Empty).Trim(), password ?? string.Empty, cancellationToken)
			.ConfigureAwait(false);

		// Neither the code nor the password, either way.
		if (result.Succeeded)
		{
			Log.Information("Password set with an emailed code");
		}
		else
		{
			Log.Warning("Password could not be set with the emailed code: {Reason}", result.Error);
		}

		return result;
	}

	/// <summary>
	/// Begin an Apple sign-in, to get the nonce the platform sheet needs.
	/// </summary>
	public Task<SignInStart?> StartAppleAsync(CancellationToken cancellationToken = default) =>
		_client.StartSignInAsync("apple", cancellationToken);

	/// <summary>
	/// Whether this build can offer Sign in with Apple at all.
	/// </summary>
	public bool IsAppleAvailable => _apple.IsAvailable;

	/// <summary>
	/// Sign out the whole way: tell the server, then forget everything.
	///
	/// <para>This is "Remove this phone" once fingerprint sign-in is set
	/// up, and plain "Sign out" until it is. Signing out behind a
	/// fingerprint is <see cref="FingerprintSignIn.Lock"/> instead, and
	/// touches none of this. The fingerprint is forgotten here too: the
	/// phone is off the list, and whoever signs in next sets up their
	/// own.</para>
	///
	/// <para>In that order, but the second half happens either way. A
	/// member who taps sign out on a phone with no signal is signed out on
	/// that phone; the device row is left live and gets revoked from the
	/// admin Devices screen. The alternative — refusing to sign out
	/// offline — is worse in the case that matters, which is somebody
	/// handing their phone to a repair shop.</para>
	/// </summary>
	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		using var attempt = AuthAttempt.Begin("Sign-out", "device");
		try
		{
			var session = await _sessions.LoadAsync().ConfigureAwait(false);

			var revoked = false;
			if (session is not null && session.IsSignedIn)
			{
				Log.Information("Signing out device {DeviceId} for member {MemberId}", session.DeviceId, session.MemberId);
				revoked = await _client.SignOutAsync(session.Token, cancellationToken).ConfigureAwait(false);
			}

			await _sessions.ClearAsync().ConfigureAwait(false);
			await _keys.ClearAsync().ConfigureAwait(false);
			_fingerprint.Forget();

			// The registration belonged to the device row that has just been
			// revoked. Leaving it standing would have the next sign-in screen
			// claim push was connected for a handset the server no longer knows.
			PushRegistered = false;

			// The handset is signed out either way. Said as a warning when
			// the server did not hear it, because that device row is still
			// live until somebody revokes it from the Devices screen.
			if (session is null || !session.IsSignedIn)
			{
				attempt.Finished(succeeded: true, "there was no session; local state cleared");
			}
			else
			{
				attempt.Finished(
					revoked,
					revoked
						? "the intergroup revoked this device; local state cleared"
						: "the intergroup did not confirm it; local state cleared, and the device row stays live until revoked");
			}
		}
		catch (Exception ex) when (attempt.Threw(ex))
		{
			throw;
		}
	}

	private async Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken)
	{
		// The push token is asked for here rather than being waited on
		// before enrolment: Firebase can take a moment to hand one over,
		// and a handset that enrols without it is not broken — it collects
		// its messages by polling until PushRegistrar reports one. An
		// enrolment blocked on Firebase would be an enrolment that fails
		// on a phone with no Play Services.
		var pushToken = await _push.CurrentTokenAsync().ConfigureAwait(false);

		// Which credential, by name only. The values are all secrets.
		Log.Information(
			"Enrolling by {Credential} as {Platform} \"{Label}\"; push token {PushToken}",
			CredentialKind(request),
			request.Platform,
			request.Label,
			string.IsNullOrEmpty(pushToken) ? "not yet issued" : "present");

		var result = await _client.EnrolAsync(
			request with { PushToken = pushToken },
			cancellationToken).ConfigureAwait(false);

		if (result.Succeeded && result.Session is not null)
		{
			await _sessions.SaveAsync(result.Session).ConfigureAwait(false);

			// Whose messages are on this phone? A handset that was refused
			// keeps its history rather than losing it to an administrator's
			// correction, so the store may already hold somebody's — and
			// this is the one moment it can be told whether that somebody
			// is the person now holding the phone. Same member, keep it;
			// anyone else, it is emptied before they see a list.
			await _history.AdoptAsync(result.Session.MemberId, cancellationToken).ConfigureAwait(false);

			// Enrolment carries the token itself rather than going through
			// RegisterPushTokenAsync, so this is the other place the
			// registration becomes true. Without it a member who has just
			// signed in would be told push was not connected until their
			// next launch, which is both wrong and the moment they are most
			// likely to be looking.
			PushRegistered = pushToken.Length > 0;

			Log.Information(
				"Enrolled as device {DeviceId} for member {MemberId}; push {Push}",
				result.Session.DeviceId,
				result.Session.MemberId,
				string.IsNullOrEmpty(pushToken) ? "unavailable (polling)" : "registered");
		}
		else
		{
			// The attempt's outcome line carries the reason at the right level. This says only
			// what was undone.
			Log.Information(
				"This handset was not enrolled ({Why}); discarding the keypair it was offered",
				string.IsNullOrEmpty(result.Error) ? "cancelled" : "refused");

			// Enrolment failed after a keypair was generated. Clearing it
			// keeps "has a key" and "is enrolled" in step, so a retry does
			// not present a key the server never accepted.
			await _keys.ClearAsync().ConfigureAwait(false);
		}

		return result;
	}

	/// <summary>
	/// Which proof an enrolment carries, for the log.
	/// </summary>
	private static string CredentialKind(EnrolmentRequest request)
	{
		if (!string.IsNullOrEmpty(request.Password))
		{
			return "password";
		}

		return string.IsNullOrEmpty(request.FirebaseToken) ? "Apple ID token" : "Firebase token";
	}

	internal static string PlatformName() =>
		DeviceInfo.Current.Platform == DevicePlatform.iOS ? "ios" : "android";

	/// <summary>
	/// What the member and their intergroup will see in the Devices list.
	///
	/// <para>The device's own name, which on Android is usually the model
	/// and on iOS is whatever its owner called it. Good enough to pick the
	/// lost one out of a list of two, which is the only thing it is
	/// for.</para>
	/// </summary>
	internal static string DeviceLabel()
	{
		var name = DeviceInfo.Current.Name;

		return string.IsNullOrWhiteSpace(name) ? DeviceInfo.Current.Model : name;
	}
}
