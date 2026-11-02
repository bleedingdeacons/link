namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Every colour a Link screen is drawn in, for one style on one platform
/// in one of light or dark.
///
/// <para><b>Nine roles, not a ramp.</b> The pages used to name colours by
/// what they were - White, Gray200, PrimaryDark - and pair them by hand
/// in an AppThemeBinding at every use. That cannot be swapped while the
/// app is running, and it spreads one decision across forty places. A
/// page now asks for what a colour is for, and the app publishes whichever
/// palette is current under these names as dynamic resources.</para>
///
/// <para>Hex strings rather than a platform colour type, so this lives
/// beside <see cref="PushStatus"/> in the core library and the contrast
/// between the roles can be checked by a test rather than by eye.</para>
/// </summary>
/// <param name="PageBackground">Behind everything.</param>
/// <param name="Surface">Things that sit above the page: menus, dropdowns.</param>
/// <param name="Row">Behind a row on the message list: the grouped card on iPhone, the page itself on Android.</param>
/// <param name="Separator">Hairlines, outlines, and the faint fill of a disabled control.</param>
/// <param name="TextPrimary">Body text, and the implicit divider at low opacity.</param>
/// <param name="TextSecondary">Placeholders and quieter text.</param>
/// <param name="Primary">Filled buttons, the unread bar, read ticks.</param>
/// <param name="OnPrimary">Text and icons on <paramref name="Primary"/>.</param>
/// <param name="Fab">The compose button.</param>
/// <param name="OnFab">The icon on <paramref name="Fab"/>.</param>
public sealed record LinkPalette(
	string PageBackground,
	string Surface,
	string Row,
	string Separator,
	string TextPrimary,
	string TextSecondary,
	string Primary,
	string OnPrimary,
	string Fab,
	string OnFab)
{
	/// <summary>
	/// The palette for a style, platform and appearance.
	///
	/// <para><b>Apple:</b> the system's own neutrals - the grouped grey
	/// page with white rows on it, true black with dark grey rows, its
	/// separator greys - with the style's colour as the tint and the
	/// compose button filled with it.</para>
	///
	/// <para><b>Android:</b> Material 3 tones generated from the style's
	/// colour, so the neutrals carry a trace of it, and the compose button
	/// is the soft primary container with dark-on-light ink rather than a
	/// solid fill.</para>
	///
	/// <para>Anything that is not Apple is drawn as Android. Link ships on
	/// those two, and a Windows build used for debugging is better off
	/// looking like the Android one than like nothing in particular.</para>
	/// </summary>
	public static LinkPalette For(LinkStyle style, bool apple, bool dark) => (style, apple, dark) switch
	{
		// A shade deeper than the splash blue. On the grouped grey page
		// #3977C3 is 4.1:1, and the Quiet buttons set their words in it.
		(LinkStyle.LinkBlue, true, false) => new(
			PageBackground: "#F2F2F7",
			Surface: "#FFFFFF",
			Row: "#FFFFFF",
			Separator: "#C6C6C8",
			TextPrimary: "#000000",
			TextSecondary: "#6C6C70",
			Primary: "#2F6AB0",
			OnPrimary: "#FFFFFF",
			Fab: "#2F6AB0",
			OnFab: "#FFFFFF"),

		// A light blue with dark ink on it. No blue takes white text and
		// still reads on the dark grey rows as well as on black: the
		// unread time is set in Primary on a row, and #3977C3 is 3.7:1
		// there. So the fill lightens and the label darkens instead.
		(LinkStyle.LinkBlue, true, true) => new(
			PageBackground: "#000000",
			Surface: "#1C1C1E",
			Row: "#1C1C1E",
			Separator: "#38383A",
			TextPrimary: "#FFFFFF",
			TextSecondary: "#98989F",
			Primary: "#6AA6EC",
			OnPrimary: "#0B1F33",
			Fab: "#6AA6EC",
			OnFab: "#0B1F33"),

		(LinkStyle.LinkBlue, false, false) => new(
			PageBackground: "#F9F9FF",
			Surface: "#EDEDF4",
			Row: "#F9F9FF",
			Separator: "#C3C6CF",
			TextPrimary: "#191C20",
			TextSecondary: "#43474E",
			Primary: "#2C5F95",
			OnPrimary: "#FFFFFF",
			Fab: "#D3E4FF",
			OnFab: "#001C38"),

		(LinkStyle.LinkBlue, false, true) => new(
			PageBackground: "#111418",
			Surface: "#1D2024",
			Row: "#111418",
			Separator: "#43474E",
			TextPrimary: "#E1E2E8",
			TextSecondary: "#C3C6CF",
			Primary: "#A1C9FF",
			OnPrimary: "#003258",
			Fab: "#1F4875",
			OnFab: "#D3E4FF"),

		(LinkStyle.IntergroupGreen, true, false) => new(
			PageBackground: "#F4F1EA",
			Surface: "#FFFDF8",
			Row: "#FFFDF8",
			Separator: "#D9D2C5",
			TextPrimary: "#1E2420",
			TextSecondary: "#5F645C",
			Primary: "#2E5E4E",
			OnPrimary: "#FFFFFF",
			Fab: "#2E5E4E",
			OnFab: "#FFFFFF"),

		(LinkStyle.IntergroupGreen, true, true) => new(
			PageBackground: "#0D1210",
			Surface: "#18201C",
			Row: "#18201C",
			Separator: "#2A3530",
			TextPrimary: "#EEF2EF",
			TextSecondary: "#8D9A93",
			Primary: "#6FBF9F",
			OnPrimary: "#0D2A20",
			Fab: "#6FBF9F",
			OnFab: "#0D2A20"),

		(LinkStyle.IntergroupGreen, false, false) => new(
			PageBackground: "#F5FBF6",
			Surface: "#E9EFEA",
			Row: "#F5FBF6",
			Separator: "#BFC9C2",
			TextPrimary: "#171D1A",
			TextSecondary: "#404944",
			Primary: "#2E6A57",
			OnPrimary: "#FFFFFF",
			Fab: "#B1F0D6",
			OnFab: "#002118"),

		(LinkStyle.IntergroupGreen, false, true) => new(
			PageBackground: "#0F1512",
			Surface: "#1B211E",
			Row: "#0F1512",
			Separator: "#404944",
			TextPrimary: "#DEE4DF",
			TextSecondary: "#BFC9C2",
			Primary: "#95D4BA",
			OnPrimary: "#003829",
			Fab: "#0D5040",
			OnFab: "#B1F0D6"),

		_ => throw new ArgumentOutOfRangeException(nameof(style), style, "No palette for this style."),
	};

	/// <summary>
	/// Each role under the resource key the XAML asks for it by. The
	/// names are the property names, so a page reads
	/// <c>{DynamicResource Primary}</c> and this is the only list.
	/// </summary>
	public IEnumerable<KeyValuePair<string, string>> Roles()
	{
		yield return new(nameof(PageBackground), PageBackground);
		yield return new(nameof(Surface), Surface);
		yield return new(nameof(Row), Row);
		yield return new(nameof(Separator), Separator);
		yield return new(nameof(TextPrimary), TextPrimary);
		yield return new(nameof(TextSecondary), TextSecondary);
		yield return new(nameof(Primary), Primary);
		yield return new(nameof(OnPrimary), OnPrimary);
		yield return new(nameof(Fab), Fab);
		yield return new(nameof(OnFab), OnFab);
	}
}
