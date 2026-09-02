using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Asks the platform for this handset's push registration token.
///
/// <para>Partial, with one half per head: Android reaches for Firebase,
/// iOS answers empty until its Firebase SDK is in place. The shared half
/// is here so there is one place that decides what an unanswered
/// registration means, and it means "poll instead" rather than
/// "broken".</para>
/// </summary>
public sealed partial class PushRegistrar : IPushRegistrar
{
	/// <summary>
	/// The current token, or empty.
	///
	/// <para>Never throws. Firebase can fail for reasons that have nothing
	/// to do with this app — no Play Services, a device with no Google
	/// account, an outage — and every one of them should leave a handset
	/// that still receives its messages on the next poll rather than one
	/// that will not sign in.</para>
	/// </summary>
	public async Task<string> CurrentTokenAsync()
	{
		try
		{
			return await PlatformTokenAsync().ConfigureAwait(false) ?? string.Empty;
		}
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
		catch (Exception)
#pragma warning restore CA1031
		{
			return string.Empty;
		}
	}

	private partial Task<string?> PlatformTokenAsync();
}
