using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

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
	private readonly FellowshipConfiguration _configuration;

	public DeviceAuthService(
		IFellowshipClient client,
		IDeviceKeyStore keys,
		ISessionStore sessions,
		IPushRegistrar push,
		FellowshipConfiguration configuration)
	{
		_client = client;
		_keys = keys;
		_sessions = sessions;
		_push = push;
		_configuration = configuration;
	}

	/// <summary>
	/// Sign in with Google: the browser leg, then the exchange.
	///
	/// <para>The browser is handed a one-time code, not a token — see
	/// Fellowship's <c>DeviceCodeStore</c> for why. This method's job is
	/// to spend that code, in this process, over TLS.</para>
	/// </summary>
	public async Task<EnrolmentResult> SignInWithGoogleAsync(CancellationToken cancellationToken = default)
	{
		var start = await _client.StartSignInAsync("google", cancellationToken).ConfigureAwait(false);
		if (start is null || !start.IsBrowserFlow)
		{
			return EnrolmentResult.Failed("This intergroup is not set up for Google sign-in.");
		}

		string code;

		try
		{
			// No cancellation token: WebAuthenticator has no overload that
			// takes one, and it does not need it — the thing that cancels
			// this is the member closing the browser tab, which arrives as
			// the TaskCanceledException caught below.
#pragma warning disable S8949
			var result = await WebAuthenticator.Default.AuthenticateAsync(
				new Uri(start.AuthorizationUrl),
				new Uri(_configuration.CallbackUrl)).ConfigureAwait(false);
#pragma warning restore S8949

			// Fellowship's callback puts either a code or an error on the
			// redirect. The error values are its own — see that
			// controller — and each of them means something a member can
			// act on, so they are translated rather than collapsed.
			if (result.Properties.TryGetValue("error", out var error))
			{
				return EnrolmentResult.Failed(Explain(error));
			}

			if (!result.Properties.TryGetValue("code", out var returned) || string.IsNullOrEmpty(returned))
			{
				return EnrolmentResult.Failed("The sign-in did not complete. Please try again.");
			}

			code = returned;
		}
		catch (TaskCanceledException)
		{
			// The member closed the browser tab. Not a failure worth an
			// error message — they know what they did.
			return EnrolmentResult.Failed(string.Empty);
		}

		return await EnrolAsync(new EnrolmentRequest
		{
			Code = code,
			PublicKey = await _keys.RegenerateAsync().ConfigureAwait(false),
			Platform = PlatformName(),
			Label = DeviceLabel(),
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Sign in with Apple: no browser leg.
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
		if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(state))
		{
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

	/// <summary>
	/// Begin an Apple sign-in, to get the nonce the platform sheet needs.
	/// </summary>
	public Task<SignInStart?> StartAppleAsync(CancellationToken cancellationToken = default) =>
		_client.StartSignInAsync("apple", cancellationToken);

	/// <summary>
	/// Sign out: tell the server, then forget everything.
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
		var session = await _sessions.LoadAsync().ConfigureAwait(false);

		if (session is not null && session.IsSignedIn)
		{
			await _client.SignOutAsync(session.Token, cancellationToken).ConfigureAwait(false);
		}

		await _sessions.ClearAsync().ConfigureAwait(false);
		await _keys.ClearAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Replace this handset's keypair after it has lost the old one.
	///
	/// <para>The recovery for "my messages will not open". It keeps the
	/// device row and its place in the intergroup's list, so nobody has to
	/// re-enrol — but messages already sent stay unreadable, because they
	/// were sealed to a key that no longer exists anywhere. Not even
	/// Fellowship can recover them: it only ever held the public half.
	/// </para>
	/// </summary>
	public async Task<bool> ReplaceKeyAsync(CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return false;
		}

		var publicKey = await _keys.RegenerateAsync().ConfigureAwait(false);

		return await _client.RotateKeyAsync(session.Token, publicKey, cancellationToken).ConfigureAwait(false);
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

		var result = await _client.EnrolAsync(
			request with { PushProvider = string.IsNullOrEmpty(pushToken) ? string.Empty : "fcm", PushToken = pushToken },
			cancellationToken).ConfigureAwait(false);

		if (result.Succeeded && result.Session is not null)
		{
			await _sessions.SaveAsync(result.Session).ConfigureAwait(false);
		}
		else
		{
			// Enrolment failed after a keypair was generated. Clearing it
			// keeps "has a key" and "is enrolled" in step, so a retry does
			// not present a key the server never accepted.
			await _keys.ClearAsync().ConfigureAwait(false);
		}

		return result;
	}

	/// <summary>
	/// Turn one of Fellowship's redirect error values into something a
	/// member can act on.
	/// </summary>
	private static string Explain(string error) => error switch
	{
		"not_a_member" =>
			"That address is not one the intergroup holds for you. Sign in with the address you gave them, or ask them to update it.",
		"declined" => "Sign-in was cancelled.",
		"verification" => "That sign-in could not be verified. Please try again.",
		_ => "The sign-in did not complete. Please try again.",
	};

	private static string PlatformName() =>
		DeviceInfo.Current.Platform == DevicePlatform.iOS ? "ios" : "android";

	/// <summary>
	/// What the member and their intergroup will see in the Devices list.
	///
	/// <para>The device's own name, which on Android is usually the model
	/// and on iOS is whatever its owner called it. Good enough to pick the
	/// lost one out of a list of two, which is the only thing it is
	/// for.</para>
	/// </summary>
	private static string DeviceLabel()
	{
		var name = DeviceInfo.Current.Name;

		return string.IsNullOrWhiteSpace(name) ? DeviceInfo.Current.Model : name;
	}
}
