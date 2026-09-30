using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The answer from a build with no intergroup to ask: never one.
///
/// <para>A build without usable settings — which is what CI makes — has no
/// site for Freedom to be on. Holding is the right state for it, and "no
/// answer" keeps it there.</para>
/// </summary>
public sealed class NoLoggingSource : ILoggingSource
{
	public static NoLoggingSource Instance { get; } = new();

	public Task<BetterStackConfiguration?> FetchAsync(DeviceSession session, CancellationToken cancellationToken = default) =>
		Task.FromResult<BetterStackConfiguration?>(null);

	public Task ForgetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<DateTimeOffset?> LastRetrievedAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<DateTimeOffset?>(null);
}
