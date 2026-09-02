using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Where the device token lives between launches.
///
/// <para>Behind an interface because the implementation is
/// <c>SecureStorage</c>, which is MAUI and therefore cannot be referenced
/// from Link.Core — see that project's file. The seam is what lets the
/// message service be tested without a keychain.</para>
/// </summary>
public interface ISessionStore
{
	Task<DeviceSession?> LoadAsync();

	Task SaveAsync(DeviceSession session);

	/// <summary>Forget the token. Signing out does this after telling the server.</summary>
	Task ClearAsync();
}
