using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The phone's own fingerprint sheet. See <see cref="IFingerprintReader"/>.
///
/// <para>Partial, with one half per head, as
/// <see cref="NotificationPermission"/> is. The shared half owns the rule
/// both obey: nothing here throws. A reader that cannot tell answers
/// "unavailable", and a sheet that could not be raised answers the same,
/// so the worst a fault here can do is send a member to sign in another
/// way.</para>
///
/// <para>Always raised on the main thread. Both platforms insist, and
/// the callers are view models that may have come off it after an
/// await.</para>
/// </summary>
public sealed partial class FingerprintReader : IFingerprintReader
{
	public bool IsAvailable
	{
		get
		{
			try
			{
				return PlatformIsAvailable();
			}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				Log.Warning(ex, "Could not tell whether this phone reads fingerprints; treating it as not");
				return false;
			}
		}
	}

	public bool CanConfirm
	{
		get
		{
			try
			{
				return PlatformCanConfirm();
			}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				Log.Warning(ex, "Could not tell whether this phone has a fingerprint or screen lock; treating it as not");
				return false;
			}
		}
	}

	public async Task<FingerprintResult> ConfirmAsync(
		string title,
		string subtitle,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await MainThread.InvokeOnMainThreadAsync(
				() => PlatformConfirmAsync(title, subtitle, cancellationToken)).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "The fingerprint sheet could not be raised");
			return FingerprintResult.Unavailable(ex.Message);
		}
	}

	private static partial bool PlatformIsAvailable();

	private static partial bool PlatformCanConfirm();

	private static partial Task<FingerprintResult> PlatformConfirmAsync(
		string title,
		string subtitle,
		CancellationToken cancellationToken);
}
