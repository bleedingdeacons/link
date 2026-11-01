namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// The two looks a member can choose between in Settings.
///
/// <para>A style is not light or dark. Each one has both, and which of
/// them is showing is the phone's own setting, followed as it changes.
/// Nor is it one palette across both platforms: each style is drawn
/// twice, once in Apple's manner and once in Material's, so an iPhone
/// and an Android handset on the same style look like the same app
/// without either looking like the other platform. See
/// <see cref="LinkPalette"/>.</para>
/// </summary>
public enum LinkStyle
{
	/// <summary>
	/// The blue Link has always had, which is also the splash colour.
	/// The default, so an upgrade changes nothing until somebody chooses.
	/// </summary>
	LinkBlue = 0,

	/// <summary>
	/// The suite green from the app icon, leading rather than accenting,
	/// on warmer surfaces.
	/// </summary>
	IntergroupGreen,
}
