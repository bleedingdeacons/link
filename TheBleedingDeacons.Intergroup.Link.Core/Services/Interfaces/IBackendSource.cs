using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>Asks the site which back end its phones should use.</summary>
public interface IBackendSource
{
	/// <summary>
	/// The site's answer, or null when it gave none: no network, an outage, or a
	/// Fellowship too old to have the route.
	/// </summary>
	Task<LinkBackend?> FetchBackendAsync(CancellationToken cancellationToken = default);
}

/// <summary>Remembers the last answer, so a phone with no network still knows where it was.</summary>
public interface IBackendStore
{
	LinkBackend Backend { get; set; }
}
