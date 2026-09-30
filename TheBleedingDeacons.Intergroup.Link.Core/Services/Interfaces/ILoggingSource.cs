using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Where a signed-in handset is told where to ship its logs.
///
/// <para>Freedom, from 2026-09-30 (see <see cref="FreedomLoggingSource"/>);
/// before that, Fellowship's <c>/logging</c> route. <see cref="RemoteLogging"/>
/// decides when to ask and what to do with the answer; this only asks.</para>
/// </summary>
public interface ILoggingSource
{
	/// <summary>
	/// Ask where this handset should ship.
	///
	/// <para>Null when there was no answer: offline, a server having a
	/// moment, the intergroup's site refusing the session. A configuration
	/// that is not valid (both fields empty, usually) is an answer: the
	/// intergroup has said not to ship. The caller keeps those two apart,
	/// because only the second should make it drop what it holds.</para>
	/// </summary>
	Task<BetterStackConfiguration?> FetchAsync(DeviceSession session, CancellationToken cancellationToken = default);

	/// <summary>
	/// Forget whatever was held for a session that has ended.
	/// </summary>
	Task ForgetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// When the settings were last confirmed by the site, whether anything
	/// had changed or not; null if they never have been. For Settings.
	/// </summary>
	Task<DateTimeOffset?> LastRetrievedAsync(CancellationToken cancellationToken = default);
}
