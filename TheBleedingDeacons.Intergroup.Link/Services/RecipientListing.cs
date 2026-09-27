using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Whether Compose lists only members with Link registered. See
/// <see cref="IRecipientListing"/> for what this does and does not change.
///
/// <para>Kept in <see cref="Preferences"/>, as <see cref="ArrivalSound"/>
/// is and for the same reason: it is a yes/no about the screen, with
/// nothing to protect, and <c>SecureStorage</c> on Android is backed by
/// the keystore, a slow place for a bool read on every keystroke of a
/// search.</para>
/// </summary>
public sealed class RecipientListing : IRecipientListing
{
	private const string PreferenceKey = "only_registered_recipients";

	/// <summary>
	/// On unless turned off. Most of a fellowship will not have Link, and
	/// a picker that opens on a page of names that cannot be tapped reads
	/// as broken rather than as informative.
	/// </summary>
	public bool OnlyRegistered
	{
		get => Preferences.Default.Get(PreferenceKey, true);
		set => Preferences.Default.Set(PreferenceKey, value);
	}
}
