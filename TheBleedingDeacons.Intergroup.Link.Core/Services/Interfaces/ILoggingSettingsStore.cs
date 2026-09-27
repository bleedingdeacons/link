using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The log-shipping settings the intergroup last handed this handset.
///
/// <para>Kept because the logger is built at process start, before
/// anything could ask Fellowship. A handset woken by a push, or opened
/// with no signal, ships with what it was last told rather than holding
/// everything until it next reaches the server.</para>
/// </summary>
public interface ILoggingSettingsStore
{
	/// <summary>
	/// What was stored, or null when this handset has not been told
	/// anything since it last signed in. A stored configuration that is
	/// not valid means the intergroup said not to ship, which is a
	/// different thing from never having been told.
	/// </summary>
	Task<BetterStackConfiguration?> LoadAsync();

	Task SaveAsync(BetterStackConfiguration configuration);

	Task ClearAsync();
}
