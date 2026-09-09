using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half.
///
/// <para>Reads the settings rather than calling
/// <c>RequestAuthorizationAsync</c>, which would raise the system prompt
/// — see the interface on why an indicator must never do that.</para>
///
/// <para><b>Provisional counts as granted.</b> It is the state an app is
/// in when iOS has let it deliver quietly without asking anybody, and
/// notifications in it do arrive — they land in the notification centre
/// instead of on the lock screen. Reporting that as "off" would send a
/// member to a settings screen to fix something that works.</para>
///
/// <para>This head has no push transport, so the indicator settles on
/// "not available" before it reaches here. It is written anyway rather
/// than stubbed to false: local notifications are what an iOS build would
/// use once its Firebase SDK is in place, and this is the half that will
/// already be right when it is.</para>
/// </summary>
public sealed partial class NotificationPermission
{
	private async partial Task<bool> PlatformIsGrantedAsync()
	{
		var settings = await UNUserNotificationCenter.Current
			.GetNotificationSettingsAsync().ConfigureAwait(false);

		return settings.AuthorizationStatus
			is UNAuthorizationStatus.Authorized
			or UNAuthorizationStatus.Provisional
			or UNAuthorizationStatus.Ephemeral;
	}
}
