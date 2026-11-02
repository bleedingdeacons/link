using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Whether pulling the message list down checks for new messages. See
/// <see cref="IPullToRefresh"/> for why it starts off.
///
/// <para>Kept in <see cref="Preferences"/>, as <see cref="RecipientListing"/>
/// is: a yes/no about the screen, with nothing to protect.</para>
/// </summary>
public sealed class PullToRefresh : IPullToRefresh
{
	private const string PreferenceKey = "pull_to_refresh";

	public bool Enabled
	{
		get => Preferences.Default.Get(PreferenceKey, false);
		set => Preferences.Default.Set(PreferenceKey, value);
	}
}
