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
	/// Whether this head carries a push transport at all. Constant per
	/// build, and answered by the platform half rather than by looking for
	/// a token — see the interface for why those are different questions.
	/// </summary>
	public bool Supported => PlatformSupported();

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

	/// <summary>
	/// A partial method rather than a partial property, and not a
	/// <c>Task</c>: whether a head was compiled with Firebase in it cannot
	/// change while the process runs, so there is nothing to await — and
	/// the analyser reads a partial property's two halves as two members,
	/// then reports the implementing one as unused and the defining one as
	/// never assigned. Both are false, and a method avoids them.
	/// </summary>
	private static partial bool PlatformSupported();
}
