using TheBleedingDeacons.Freedom.Client.Abstractions;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// What Freedom is told about this handset when Link hands its session
/// over: the same name, platform and model Fellowship's Devices list shows,
/// and the app version.
///
/// <para>Without it the handover sent nothing, and every Link handset sat
/// in the site's Freedom Devices tab as "(unnamed)" with no model — no way
/// to tell one from another.</para>
///
/// <para><b>No device id.</b> A handed-over session is keyed on its
/// Fellowship enrolment, not on anything the handset asserts, so the id is
/// left empty and the server never reads it on that route.</para>
///
/// <para>The name is sent when the session is handed over, so a handset
/// already signed in to Freedom keeps what it was enrolled with until it
/// next hands over.</para>
/// </summary>
public sealed class LinkDeviceIdentity : IDeviceIdentity
{
	public Task<DeviceIdentity> GetAsync(CancellationToken cancellationToken) =>
		Task.FromResult(new DeviceIdentity(
			DeviceId: string.Empty,
			Platform: DeviceAuthService.PlatformName(),
			Label: DeviceAuthService.DeviceLabel(),
			Model: DeviceInfo.Current.Model,
			AppVersion: AppInfo.Current.VersionString));
}
