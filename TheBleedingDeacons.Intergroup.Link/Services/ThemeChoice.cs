using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Keeps the app's colours in step with the member's style and the
/// phone's light or dark setting. See <see cref="IThemeChoice"/>.
///
/// <para><b>Dynamic resources, republished.</b> Every page and style asks
/// for a role - <c>{DynamicResource Primary}</c> - and this writes the
/// current <see cref="LinkPalette"/> into the application's resources
/// under those names. Writing a key repaints everything bound to it, so
/// a change of style, or the phone going dark at sunset, shows on the
/// open screen without rebuilding it. The AppThemeBinding pairs this
/// replaces resolved once, when a page was built, and could follow light
/// and dark but never a second palette.</para>
///
/// <para>Each role is published twice, as a Color and as a Brush with
/// <c>Brush</c> on the end, because <c>Border.Stroke</c> and
/// <c>Shape.Fill</c> take a Brush and a dynamic resource is not
/// converted on its way in the way a static one is.</para>
///
/// <para>Kept in <see cref="Preferences"/>, as <see cref="RecipientListing"/>
/// is: it is a choice about the screen with nothing to protect.</para>
/// </summary>
public sealed class ThemeChoice : IThemeChoice
{
	private const string PreferenceKey = "link_style";

	private bool _following;

	/// <summary>
	/// The stored style, or the blue for a value this build does not know
	/// - one written by a later version and then downgraded.
	/// </summary>
	public LinkStyle Style
	{
		get
		{
			var stored = (LinkStyle)Preferences.Default.Get(PreferenceKey, (int)LinkStyle.LinkBlue);

			return Enum.IsDefined(stored) ? stored : LinkStyle.LinkBlue;
		}
		set
		{
			Preferences.Default.Set(PreferenceKey, (int)value);
			Apply();
		}
	}

	/// <summary>
	/// Publish the current palette, and from the first call on, publish
	/// it again whenever the phone switches between light and dark.
	/// Called once from <see cref="App"/>, before the first page is built.
	/// </summary>
	public void Apply()
	{
		if (Application.Current is not { } app)
		{
			return;
		}

		if (!_following)
		{
			_following = true;
			app.RequestedThemeChanged += (_, _) => Apply();
		}

		var dark = app.RequestedTheme == AppTheme.Dark;

		var palette = LinkPalette.For(
			Style,
			apple: DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst,
			dark: dark);

		foreach (var (role, hex) in palette.Roles())
		{
			var colour = Color.FromArgb(hex);
			app.Resources[role] = colour;
			app.Resources[role + "Brush"] = new SolidColorBrush(colour);
		}

		InkTheSystemBars(dark);
	}

	/// <summary>
	/// Dark icons in the status and navigation bars on a light page, light
	/// icons on a dark one.
	///
	/// <para><b>Android draws the page behind the bars</b> (see
	/// MainActivity), so the bars have no colour of their own and the icons
	/// sit straight on the page. Left to the platform they stayed white,
	/// and on a light page the clock and battery vanished. iOS picks the
	/// status bar's ink from the appearance by itself, so this is
	/// Android's alone.</para>
	///
	/// <para>Does nothing before there is an activity: the first
	/// <see cref="Apply"/>, from the App constructor, runs before Android
	/// has created one, so MainActivity calls this again once it has.</para>
	/// </summary>
	private static void InkTheSystemBars(bool dark)
	{
#if ANDROID
		if (Platform.CurrentActivity?.Window is not { } window)
		{
			return;
		}

		var bars = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
		bars.AppearanceLightStatusBars = !dark;
		bars.AppearanceLightNavigationBars = !dark;
#endif
	}
}
