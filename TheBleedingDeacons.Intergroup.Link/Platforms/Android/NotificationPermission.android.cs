using AndroidX.Core.App;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Android half.
///
/// <para><b><c>AreNotificationsEnabled</c> rather than a POST_NOTIFICATIONS
/// permission check.</b> They are not the same question, and
/// <see cref="MainActivity"/> asks the other one. The runtime permission
/// only exists on API 33 and above, so checking it reports "granted" on
/// every older phone whatever its owner has done — and on a new one it
/// stays granted after Link's notifications are switched off from the
/// phone's own settings screen, which is the ordinary way people silence
/// an app. This call covers both, and it is what the system itself
/// consults before deciding whether to show anything.</para>
/// </summary>
public sealed partial class NotificationPermission
{
	// Platform.AppContext rather than Android.App.Application.Context,
	// which the binding declares nullable. The compat manager itself is
	// declared nullable too, and false is the right answer if it ever is:
	// no manager, nothing that could show a notification.
	private partial Task<bool> PlatformIsGrantedAsync() =>
		Task.FromResult(
			NotificationManagerCompat.From(Platform.AppContext)?.AreNotificationsEnabled() ?? false);
}
