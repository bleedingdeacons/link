using TheBleedingDeacons.Intergroup.Link.Services;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Reads this handset's four facts and hands them to
/// <see cref="UserAgent"/>.
/// </summary>
/// <remarks>
/// <para>Split from the builder because the builder lives in Link.Core,
/// which has no MAUI workload and therefore cannot see <c>AppInfo</c> or
/// <c>DeviceInfo</c>. The string building is the part worth testing and it
/// went where a test project can reach it; this is the part that can only
/// run on a device.</para>
///
/// <para>The address comes from <c>LinkServices</c> rather than the DI
/// container for the reason that class exists: the Android push service
/// runs with no MAUI host, and this is read on its path too. A build
/// shipped without usable settings answers an empty base URL, and the
/// header says "unknown" rather than guessing.</para>
/// </remarks>
internal static class AppUserAgent
{
	/// <summary>
	/// The header as it stands right now. A method rather than a property
	/// because it is the callback a <see cref="UserAgentHandler"/> holds.
	/// </summary>
	public static string Current() => UserAgent.ForApp(
		UserAgent.Product,
		AppInfo.Current.VersionString,
		DeviceInfo.Current.Platform.ToString(),
		LinkServices.Configuration.BaseUrl);
}
