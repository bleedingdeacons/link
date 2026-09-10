using System.Globalization;
using Foundation;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using UIKit;
using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Link;

/// <summary>
/// The iOS application delegate.
///
/// <para>The Android counterpart of the two sign-in overrides below is
/// <c>WebAuthenticatorCallbackActivity</c>: both exist so the browser leg
/// of Google sign-in can hand its one-time code back to the app. Without
/// them iOS opens the sign-in page, the member signs in, and the callback
/// to <c>link://auth</c> lands nowhere — a failure that looks like the
/// server refusing the sign-in rather than the app failing to catch the
/// answer.</para>
///
/// <para><b>Push is handled here rather than in a service of its own</b>,
/// which is the shape of the difference between the platforms. Android
/// starts <c>LinkFirebaseMessagingService</c> with no MAUI host and
/// possibly no app; iOS has no such thing — a background push runs the app
/// process — so the delegate is the whole of it.</para>
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	/// <summary>
	/// How long a pushed message gets to be opened and shown.
	///
	/// <para>iOS allows roughly thirty seconds for a background push before
	/// it stops listening; well inside that, because the work is a keychain
	/// read, an RSA unwrap and an AES open, and anything slower than this
	/// is not slow but stuck. The message is already on the server either
	/// way, so the cost of giving up is that it arrives on the next poll.
	/// The Android half budgets the same.</para>
	/// </summary>
	private static readonly TimeSpan DeliveryBudget = TimeSpan.FromSeconds(15);

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Base first: it builds the MAUI app, so Serilog is standing before
		// Firebase has anything to say.
		var launched = base.FinishedLaunching(application, launchOptions);

		// Never throws — a build with no Firebase configuration reports
		// unavailable and the handset polls.
		FirebasePush.Configure();

		return launched;
	}

	/// <summary>
	/// The custom-scheme return leg: <c>link://auth</c> coming back from
	/// the system browser.
	/// </summary>
	public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options) =>
		Platform.OpenUrl(application, url, options);

	/// <summary>
	/// The universal-link return leg. Nothing uses it today — the callback
	/// is a custom scheme — but WebAuthenticator's contract is that both
	/// are forwarded, and a half-wired delegate is the kind of thing that
	/// works until the day the callback shape changes.
	/// </summary>
	public override bool ContinueUserActivity(
		UIApplication application,
		NSUserActivity userActivity,
		UIApplicationRestorationHandler completionHandler) =>
		Platform.ContinueUserActivity(application, userActivity, completionHandler);

	/// <summary>
	/// Apple has issued this installation an APNs device token.
	/// </summary>
	/// <remarks>
	/// <para>Handed straight to Firebase, which cannot produce an FCM
	/// registration token until it has one.</para>
	///
	/// <para><b>Exported rather than overridden, and it has to be.</b>
	/// <c>MauiUIApplicationDelegate</c> derives from <c>UIResponder</c> and
	/// <i>implements</i> <c>IUIApplicationDelegate</c> — it does not inherit
	/// the <c>UIApplicationDelegate</c> class where these are virtual, so
	/// there is nothing to override and the compiler says so (CS0115). What
	/// makes iOS call this is the selector matching the one Apple invokes;
	/// the C# name is ours. Renaming the method is safe, changing the string
	/// is not.</para>
	/// </remarks>
	[Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
	public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
	{
		FirebasePush.SetApnsToken(deviceToken);
	}

	/// <summary>
	/// Apple refused to issue one — usually a build signed without the push
	/// entitlement, a simulator, or no network. All of them leave a handset
	/// that still collects its messages on the poll.
	/// </summary>
	[Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
	public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
	{
		Log.Warning(
			"Apple refused to register this handset for remote notifications ({Error}); it will poll only",
			error?.LocalizedDescription ?? "no reason given");
	}

	/// <summary>
	/// A message has been pushed to this handset.
	/// </summary>
	/// <remarks>
	/// <para>Fellowship sends a <b>silent</b> push: <c>content-available</c>
	/// and the two envelope fields, with no <c>alert</c> at all. So nothing
	/// is displayed unless this runs — the app opens the envelope with the
	/// private key that never leaves the handset, stores the message, and
	/// raises the notification itself.</para>
	///
	/// <para>That is the same arrangement as Android's, and for the same
	/// reason: the server cannot write the notification because the server
	/// cannot read the message.</para>
	///
	/// <para><b>The completion handler must be called on every path.</b> iOS
	/// measures how long the app takes and how often it says it had nothing,
	/// and throttles background delivery accordingly — a path that forgets
	/// to answer is a handset that is pushed to less and less often, which
	/// would look like an intermittent server.</para>
	/// </remarks>
	[Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
	public void DidReceiveRemoteNotification(
		UIApplication application,
		NSDictionary userInfo,
		Action<UIBackgroundFetchResult> completionHandler)
	{
		ArgumentNullException.ThrowIfNull(completionHandler);

		var wrappedKey = StringFrom(userInfo, "k");
		var payload = StringFrom(userInfo, "p");

		if (wrappedKey.Length == 0 || payload.Length == 0)
		{
			// Not one of ours, or a Fellowship older than this build.
			// Ignored rather than reported: the poll will collect whatever
			// it was.
			completionHandler(UIBackgroundFetchResult.NoData);
			return;
		}

		_ = Task.Run(async () =>
		{
			var result = UIBackgroundFetchResult.NoData;

			try
			{
				// The session is checked rather than assumed: a push
				// arriving for a signed-out handset is a stale token at the
				// server, and the right answer is to do nothing quietly.
				// Android's HeadlessMessages.Resolve makes the same check
				// for the same reason.
				var session = await LinkServices.Sessions.LoadAsync().ConfigureAwait(false);
				if (session is null || !session.IsSignedIn)
				{
					return;
				}

				var stored = await LinkServices.Messages
					.ReceivePushAsync(wrappedKey, payload)
					.WaitAsync(DeliveryBudget)
					.ConfigureAwait(false);

				if (stored is null)
				{
					// The envelope would not open — almost always a keypair
					// this handset has lost. Nothing is shown, because "New
					// message" for something the app cannot display is worse
					// than silence. The next sync reports the key fault.
					return;
				}

				Notify(stored);
				result = UIBackgroundFetchResult.NewData;
			}
			catch (TimeoutException ex)
			{
				Log.Warning(
					ex,
					"A pushed message took longer than {Budget} to open; leaving it for the poll",
					DeliveryBudget);
				result = UIBackgroundFetchResult.Failed;
			}
#pragma warning disable CA1031 // Deliberately broad: see below.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				// Throwing out of a background-push task would take the
				// process with it, and the message is already safe on the
				// server — the next poll fetches it. Logged rather than
				// swallowed silently, because the sink cannot throw back.
				Log.Error(ex, "A pushed message could not be delivered; leaving it for the poll");
				result = UIBackgroundFetchResult.Failed;
			}
			finally
			{
				// On the main thread: UIKit is not thread-safe and this
				// handler is UIKit's.
				MainThread.BeginInvokeOnMainThread(() => completionHandler(result));
			}
		});
	}

	/// <summary>
	/// Put the message in the notification centre.
	/// </summary>
	/// <remarks>
	/// <para><b>Never the message itself.</b> The sender's name and the
	/// words "New message", and nothing else — sealing the payload end to
	/// end would be pointless if the app then printed it on the lock
	/// screen. The Android half shows exactly the same two things, and
	/// takes the same care with its public version.</para>
	///
	/// <para>Keyed on the message id, so the same message arriving twice —
	/// pushed, then polled — replaces its own notification rather than
	/// stacking a second one.</para>
	/// </remarks>
	private static void Notify(LinkMessage message)
	{
		using var content = new UNMutableNotificationContent
		{
			Title = string.IsNullOrEmpty(message.Sender) ? "New message" : message.Sender,
			Body = "New message",
			Sound = UNNotificationSound.Default,
		};

		var request = UNNotificationRequest.FromIdentifier(
			message.Id.ToString(CultureInfo.InvariantCulture),
			content,
			trigger: null);

		UNUserNotificationCenter.Current.AddNotificationRequest(request, error =>
		{
			if (error is not null)
			{
				Log.Warning("A pushed message could not be shown: {Error}", error.LocalizedDescription);
			}
		});
	}

	/// <summary>
	/// One string out of the userInfo dictionary, or empty.
	/// </summary>
	/// <remarks>
	/// Everything in an FCM data message arrives as a string, but the
	/// dictionary is typed object-to-object and a push is not something to
	/// trust the shape of — this runs before anything else has validated
	/// it.
	/// </remarks>
	private static string StringFrom(NSDictionary? userInfo, string key)
	{
		if (userInfo is null)
		{
			return string.Empty;
		}

		using var nsKey = new NSString(key);

		return userInfo.TryGetValue(nsKey, out var value) && value is NSString text
			? text.ToString()
			: string.Empty;
	}
}
