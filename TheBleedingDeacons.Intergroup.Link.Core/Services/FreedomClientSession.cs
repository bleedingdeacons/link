using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using EnrolmentResult = TheBleedingDeacons.Freedom.Client.EnrolmentResult;
using SyncResult = TheBleedingDeacons.Freedom.Client.SyncResult;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// <see cref="IFreedomSession"/> over the real client.
/// </summary>
public sealed class FreedomClientSession(FreedomClient client) : IFreedomSession
{
	private readonly FreedomClient _client = client ?? throw new ArgumentNullException(nameof(client));

	public Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default) => _client.SyncAsync(cancellationToken);

	public Task<EnrolmentResult> EnrolAsync(FreedomProof proof, CancellationToken cancellationToken = default) =>
		_client.EnrolAsync(proof, cancellationToken);

	public string? Get(string key) => _client.Get(key);

	public Task SignOutAsync(CancellationToken cancellationToken = default) => _client.SignOutAsync(cancellationToken);

	// From the store rather than the client's last snapshot: Settings can be
	// opened before this process's first sync has finished.
	public async Task<DateTimeOffset?> LastRetrievedAsync(CancellationToken cancellationToken = default) =>
		(await _client.LoadAsync(cancellationToken).ConfigureAwait(false)).VerifiedAt;
}
