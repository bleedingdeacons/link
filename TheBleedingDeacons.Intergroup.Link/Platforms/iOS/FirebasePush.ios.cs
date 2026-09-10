using Firebase.CloudMessaging;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UIKit;
using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Firebase Cloud Messaging on the iOS head.
/// </summary>
/// <remarks>
/// <para><b>Why a binding is needed at all.</b> iOS hands the app an
/// <i>APNs device token</i>. Fellowship sends through FCM, and
/// <c>message.token</c> requires an <i>FCM registration token</i> — a
/// different identifier, which FCM rejects if given the wrong one.
/// Firebase is the thing that exchanges one for the other.</para>
///
/// <para><b>Absence is a documented state, not a failure.</b> Without
/// <c>GoogleService-Info.plist</c> in the bundle there is no Firebase
/// project to register against, and this reports unavailable rather than
/// throwing — exactly as the Android head does without
/// <c>google-services.json</c>. A handset in that state collects its
/// messages by polling, which is the same position as a phone in a
/// tunnel: everything arrives, on the poll interval rather than at
/// once.</para>
///
/// <para>Kept deliberately close to Hand's <c>Apple/FirebasePush.cs</c>,
/// which does the same job against the same binding. The one difference
/// is what arrives: Fellowship sends a <i>silent</i> push — no alert, just
/// <c>content-available</c> and the sealed envelope — and the app builds
/// the notification itself. Hand is sent a visible alert with a sound the
/// system plays.</para>
/// </remarks>
internal static class FirebasePush
{
	/// <summary>
	/// The bundle resource Firebase reads its project configuration from.
	/// Named here rather than left to Firebase so its absence can be
	/// detected before <c>App.Configure()</c> is called.
	/// </summary>
	private const string ConfigResource = "GoogleService-Info";

	private static readonly TokenWatcher Watcher = new();

	/// <summary>
	/// Set once iOS has handed over the APNs token and it has been given
	/// to Firebase. Until then there is nothing to exchange.
	/// </summary>
	private static bool _apnsTokenSet;

	/// <summary>
	/// Whether this build has a Firebase project behind it and configured
	/// cleanly. False means poll-only, and <c>PushRegistrar</c> reports no
	/// transport rather than claiming one — which is what lets the
	/// settings screen tell a member "this build has no push" instead of
	/// "not registered yet", an invitation to wait for something that is
	/// never coming.
	/// </summary>
	public static bool Available { get; private set; }

	/// <summary>
	/// Start Firebase, if this build has the configuration for it. Called
	/// from the app delegate at launch, and safe to call more than once.
	/// </summary>
	public static void Configure()
	{
		if (Available)
		{
			return;
		}

		// Checked rather than caught. Firebase raises an Objective-C
		// exception for a missing plist, and relying on an exception to
		// discover a supported configuration is how a launch crash gets
		// shipped.
		if (NSBundle.MainBundle.PathForResource(ConfigResource, "plist") is null)
		{
			Log.Warning(
				"No {Resource}.plist in the bundle, so there is no Firebase project to register with. "
				+ "This handset will collect its messages by polling.",
				ConfigResource);

			return;
		}

		try
		{
			Firebase.Core.App.Configure();
			Messaging.SharedInstance.Delegate = Watcher;
			Available = true;

			Log.Information("Firebase configured; this handset can be pushed to");
		}
#pragma warning disable CA1031 // Deliberately broad: nothing here is worth a launch crash.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Error(ex, "Firebase could not be configured; this handset will poll only");
		}
	}

	/// <summary>
	/// Hand Firebase the APNs token iOS has just issued.
	/// </summary>
	public static void SetApnsToken(NSData deviceToken)
	{
		if (!Available)
		{
			return;
		}

		Messaging.SharedInstance.ApnsToken = deviceToken;
		_apnsTokenSet = true;
	}

	/// <summary>
	/// The FCM registration token for this install, or empty.
	/// </summary>
	/// <remarks>
	/// Three things have to happen in order: the member must permit
	/// notifications, iOS must issue an APNs token, and only then can
	/// Firebase exchange it. Each step is bounded, because enrolment is
	/// waiting on this and a handset with no signal must still be able to
	/// sign in — it enrols without a token and registers one later.
	/// </remarks>
	public static async Task<string?> TokenAsync()
	{
		if (!Available)
		{
			return null;
		}

		try
		{
			var (granted, error) = await UNUserNotificationCenter.Current
				.RequestAuthorizationAsync(
					UNAuthorizationOptions.Alert
					| UNAuthorizationOptions.Badge
					| UNAuthorizationOptions.Sound)
				.ConfigureAwait(false);

			if (error is not null)
			{
				Log.Warning("Notification authorisation failed: {Error}", error.LocalizedDescription);
			}

			if (!granted)
			{
				// Refused, or refused earlier and remembered. Registering
				// anyway would still produce a token, and Fellowship would
				// then spend a send on every message for a phone that shows
				// nothing. The messages still arrive on the poll.
				Log.Warning(
					"Notifications are not permitted on this handset, so it will poll only. "
					+ "A member can allow them in iOS Settings and sign in again.");

				return null;
			}

			await MainThread.InvokeOnMainThreadAsync(
				UIApplication.SharedApplication.RegisterForRemoteNotifications).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberately broad: enrolment must not fail over this.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Remote notification registration could not be started");
			return null;
		}

		// Registration is a round trip to Apple. Bounded, because a handset
		// with no network would otherwise never finish signing in.
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		while (!_apnsTokenSet && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(200).ConfigureAwait(false);
		}

		if (!_apnsTokenSet)
		{
			Log.Warning("Apple did not issue an APNs token in time; enrolling without one for now");
			return null;
		}

		try
		{
			// FetchToken rather than reading FcmToken, which is null until
			// the first exchange has happened — and would therefore report
			// no token on the very launch that enrols.
			return await Messaging.SharedInstance.FetchTokenAsync().ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberately broad: see above.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "FCM registration token could not be obtained; this handset will poll only");
			return null;
		}
	}

	/// <summary>
	/// Hears about token rotations.
	/// </summary>
	/// <remarks>
	/// <para>Tokens rotate without warning, and a stale one at the server
	/// is why a handset silently stops being pushed to — so a new one is
	/// sent on immediately, with <c>DeviceAuthService.RestoreAsync</c> at
	/// every launch as the backstop.</para>
	///
	/// <para>Resolved from the container rather than built from
	/// <c>LinkServices</c> the way Android's <c>HeadlessMessages</c> does
	/// it. That class exists because Android starts a push service with no
	/// MAUI host at all; iOS has no such thing — a background push runs
	/// the app process — so the container is there to ask.</para>
	/// </remarks>
	private sealed class TokenWatcher : MessagingDelegate
	{
		public override void DidReceiveRegistrationToken(Messaging messaging, string? fcmToken)
		{
			if (string.IsNullOrEmpty(fcmToken))
			{
				// Firebase reports null when it has retired a token without
				// yet issuing another. The next call brings the replacement.
				return;
			}

			Log.Information("Firebase issued a new push token; registering it");

			var auth = IPlatformApplication.Current?.Services.GetService<DeviceAuthService>();
			if (auth is null)
			{
				// Too early in launch for the container to exist. Registered
				// at the next opportunity by RestoreAsync, which sends the
				// current token unconditionally for this reason.
				return;
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await auth.RegisterPushTokenAsync(fcmToken).ConfigureAwait(false);
				}
#pragma warning disable CA1031 // Deliberately broad: a background rotation must not crash the app.
				catch (Exception ex)
#pragma warning restore CA1031
				{
					Log.Error(ex, "New push token could not be registered with Fellowship");
				}
			});
		}
	}
}
