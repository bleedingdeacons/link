namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// The looks a member can choose between in Settings.
///
/// <para>A style is not light or dark. Each one has both, and which of
/// them is showing is the phone's own setting, followed as it changes.
/// Nor is it one palette across both platforms: each style is drawn
/// twice, once in Apple's manner and once in Material's, so an iPhone
/// and an Android handset on the same style look like the same app
/// without either looking like the other platform. See
/// <see cref="LinkPalette"/>.</para>
///
/// <para>Except <see cref="StarshipConsole"/> and <see cref="SiloTerminal"/>,
/// which are neither: each is a look of its own rather than a colour for
/// the platform's look, so it is the same on both, and it is always dark.
/// See <see cref="LinkStyles"/>.</para>
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

	/// <summary>
	/// A starship's computer console from late-eighties television:
	/// orange, peach and lilac on black, with pill-ended buttons and
	/// tall, narrow lettering.
	///
	/// <para>Named for what it looks like rather than for the show's own
	/// word for its interface, which is a trademark; the colours are
	/// nobody's.</para>
	/// </summary>
	StarshipConsole,

	/// <summary>
	/// The computer terminal of an underground silo: phosphor green on a
	/// black screen, square buttons lit in inverse video, and fixed-width
	/// lettering, after the green-screen machines of the television
	/// series set in one.
	///
	/// <para>Appended rather than placed beside the console, because the
	/// choice is kept as this number and a member on the console must
	/// stay on it.</para>
	/// </summary>
	SiloTerminal,
}

/// <summary>
/// What a style decides beyond its colours. Kept beside the enum, in the
/// core library, so a test can hold it to the palette.
/// </summary>
public static class LinkStyles
{
	/// <summary>
	/// Whether the style is dark whatever the phone is set to. The app
	/// then asks the platform for dark too, so the status bar's ink and
	/// the system's own sheets and pickers match the page rather than
	/// the phone.
	/// </summary>
	public static bool AlwaysDark(this LinkStyle style) => style is LinkStyle.StarshipConsole or LinkStyle.SiloTerminal;

	/// <summary>
	/// The corner radius of a button drawn by the app's own button
	/// styles. 22 is half the 44 they are at least as tall as, so the
	/// console's buttons come out as pills. The terminal's are square: a
	/// block of lit screen, like a highlighted menu item.
	/// </summary>
	public static int ButtonCornerRadius(this LinkStyle style) => style switch
	{
		LinkStyle.StarshipConsole => 22,
		LinkStyle.SiloTerminal => 0,
		_ => 8,
	};

	/// <summary>
	/// The alias, as MauiProgram registers it, of the font the app's
	/// text is set in. The console's is Antonio, the nearest free face
	/// to the tall, narrow lettering on the screens it is drawn from. The
	/// terminal's is IBM Plex Mono: fixed-width like a character screen,
	/// but at ordinary proportions, so it comes out as large as Open Sans
	/// at the same size and the type scale holds. A pixel face would look
	/// closer and read a size smaller.
	/// </summary>
	public static string BodyFontFamily(this LinkStyle style) => style switch
	{
		LinkStyle.StarshipConsole => "AntonioRegular",
		LinkStyle.SiloTerminal => "IBMPlexMonoRegular",
		_ => "OpenSansRegular",
	};
}
