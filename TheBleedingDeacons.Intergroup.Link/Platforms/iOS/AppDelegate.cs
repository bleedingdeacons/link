using Foundation;
using UIKit;

namespace TheBleedingDeacons.Intergroup.Link;

/// <summary>
/// The iOS application delegate.
///
/// <para>The Android counterpart of the two overrides below is
/// <c>WebAuthenticatorCallbackActivity</c>: both exist so the browser leg
/// of Google sign-in can hand its one-time code back to the app. Without
/// them iOS opens the sign-in page, the member signs in, and the callback
/// to <c>link://auth</c> lands nowhere — a failure that looks like the
/// server refusing the sign-in rather than the app failing to catch the
/// answer.</para>
///
/// <para><b>No push handling here, deliberately.</b> This head has no
/// Firebase iOS SDK and no APNs key, so there is no
/// <c>RegisteredForRemoteNotifications</c> to answer and nothing would
/// call it. An iOS build polls; see
/// <c>Platforms/iOS/PushRegistrar.ios.cs</c>, which is where that decision
/// is stated in code, and README.md, "What is not done".</para>
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>
	/// The custom-scheme return leg: <c>link://auth</c> coming back from
	/// the system browser.
	/// </summary>
	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options) =>
		Platform.OpenUrl(app, url, options);

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
}
