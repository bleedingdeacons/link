using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Which <see cref="LinkStyle"/> this handset is drawn in.
///
/// <para>The member's own choice, on this handset, like
/// <see cref="IRecipientListing"/>. Setting it repaints every open screen
/// at once; there is no restart and nothing to confirm.</para>
/// </summary>
public interface IThemeChoice
{
	/// <summary>The current style. Persisted, and applied as soon as it is set.</summary>
	LinkStyle Style { get; set; }
}
