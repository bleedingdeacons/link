using TheBleedingDeacons.Freedom.Client;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The four things Link asks of Freedom's client, as a seam.
///
/// <para><see cref="FreedomClient"/> is a concrete class over a real server;
/// this is what lets <see cref="FreedomLoggingSource"/>'s decisions — when to
/// hand the session over, which answers count — be tested without one. Its
/// only implementation is <see cref="FreedomClientSession"/>.</para>
/// </summary>
public interface IFreedomSession
{
	// Qualified: this namespace has a SyncResult and an EnrolmentResult of
	// its own, for Fellowship, and they would win over a using alias.
	Task<TheBleedingDeacons.Freedom.Client.SyncResult> SyncAsync(CancellationToken cancellationToken = default);

	Task<TheBleedingDeacons.Freedom.Client.EnrolmentResult> EnrolAsync(FreedomProof proof, CancellationToken cancellationToken = default);

	string? Get(string key);

	Task SignOutAsync(CancellationToken cancellationToken = default);
}
