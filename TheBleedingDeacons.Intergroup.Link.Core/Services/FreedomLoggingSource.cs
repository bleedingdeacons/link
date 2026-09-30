using Serilog;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Better Stack settings, from the site's Freedom plugin.
///
/// <para><b>One sign-in, not two.</b> Link has already proved a Google
/// account to Fellowship, and holds a device token for it. Freedom accepts
/// that token in place of a second sign-in — a session handover — for an
/// application with "Accept a Link session" ticked, which the <c>link</c>
/// application on the site is. The handset becomes a Freedom tablet keyed
/// on its Fellowship enrolment, and goes when that enrolment goes.</para>
///
/// <para><b>Two keys:</b> <c>betterstack.endpoint</c> and
/// <c>betterstack.source_token</c>, the second ticked Secret on the site so
/// it arrives sealed to this handset's own key. Register reads the same two
/// names from its own application.</para>
///
/// <para>Replaces Fellowship's <c>/logging</c> route, which served the same
/// two values to the same handsets. Freedom is where an app's settings live
/// now; one place to set them, per application, beats a Fellowship setting
/// that only Link could read.</para>
/// </summary>
public sealed class FreedomLoggingSource : ILoggingSource
{
	public const string EndpointKey = "betterstack.endpoint";

	public const string SourceTokenKey = "betterstack.source_token";

	private readonly IFreedomSession _freedom;
	private readonly IHandoverRecord _handover;

	public FreedomLoggingSource(IFreedomSession freedom, IHandoverRecord handover)
	{
		_freedom = freedom ?? throw new ArgumentNullException(nameof(freedom));
		_handover = handover ?? throw new ArgumentNullException(nameof(handover));
	}

	public async Task<BetterStackConfiguration?> FetchAsync(DeviceSession session, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);

		var sync = await _freedom.SyncAsync(cancellationToken).ConfigureAwait(false);

		// Not a Freedom tablet yet, or not any more — a new Fellowship
		// enrolment is a new tablet. Hand the session over. Anything else
		// that is not a clean sync is no answer: Freedom keeps what it had,
		// and so does the logger.
		if (sync.Status is SyncStatus.NotEnrolled or SyncStatus.Revoked)
		{
			var enrolled = await _freedom.EnrolAsync(FreedomProof.ExistingSession(session.Token), cancellationToken).ConfigureAwait(false);
			if (!enrolled.Succeeded)
			{
				Log.Information("Freedom did not take this handset's session ({Status}): {Message}", enrolled.Status, enrolled.Message);
				return null;
			}

			_handover.MarkCurrent();
			sync = enrolled.Sync ?? await _freedom.SyncAsync(cancellationToken).ConfigureAwait(false);
		}
		else if (IsAnswer(sync.Status) && !_handover.IsCurrent)
		{
			// Signed in already, but not since Link was updated: hand over
			// once more so Freedom has this build's name, model and version.
			// See IHandoverRecord. It re-attaches the same device; if it is
			// refused, the sync already in hand still counts.
			var refreshed = await _freedom.EnrolAsync(FreedomProof.ExistingSession(session.Token), cancellationToken).ConfigureAwait(false);
			if (refreshed.Succeeded)
			{
				_handover.MarkCurrent();
				sync = refreshed.Sync ?? sync;
			}
			else
			{
				Log.Information("Freedom did not refresh this handset's details ({Status}): {Message}", refreshed.Status, refreshed.Message);
			}
		}

		// A key fault is a secret this handset could not open. Freedom kept
		// the value it had, which is still the best answer there is.
		if (!IsAnswer(sync.Status))
		{
			Log.Debug("Freedom gave no answer ({Status}); keeping where logs go", sync.Status);
			return null;
		}

		return new BetterStackConfiguration
		{
			Endpoint = _freedom.Get(EndpointKey) ?? string.Empty,
			SourceToken = _freedom.Get(SourceTokenKey) ?? string.Empty,
		};
	}

	private static bool IsAnswer(SyncStatus status) =>
		status is SyncStatus.UpToDate or SyncStatus.Updated or SyncStatus.KeyFault;

	public Task ForgetAsync(CancellationToken cancellationToken = default) =>
		_freedom.SignOutAsync(cancellationToken);

	public Task<DateTimeOffset?> LastRetrievedAsync(CancellationToken cancellationToken = default) =>
		_freedom.LastRetrievedAsync(cancellationToken);
}
