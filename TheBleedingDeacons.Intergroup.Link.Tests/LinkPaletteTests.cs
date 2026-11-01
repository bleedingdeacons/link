using System.Globalization;
using TheBleedingDeacons.Intergroup.Link.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The eight palettes, checked for what nobody checks by eye on every
/// change: that text can be read on what it sits on.
///
/// <para>The thresholds are WCAG's. 4.5:1 for anything set as text, which
/// here includes a button label at 14; 3:1 for a coloured mark that is
/// not text - the unread bar, the read ticks - against the page.</para>
/// </summary>
public class LinkPaletteTests
{
	public static TheoryData<LinkStyle, bool, bool> Every()
	{
		var data = new TheoryData<LinkStyle, bool, bool>();

		foreach (var style in Enum.GetValues<LinkStyle>())
		{
			foreach (var apple in new[] { true, false })
			{
				foreach (var dark in new[] { false, true })
				{
					data.Add(style, apple, dark);
				}
			}
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Every))]
	public void Every_role_is_a_six_digit_colour(LinkStyle style, bool apple, bool dark)
	{
		foreach (var (role, hex) in LinkPalette.For(style, apple, dark).Roles())
		{
			Assert.Matches("^#[0-9A-F]{6}$", hex);
			Assert.False(string.IsNullOrEmpty(role));
		}
	}

	[Theory]
	[MemberData(nameof(Every))]
	public void Text_can_be_read_on_the_page_and_on_surfaces(LinkStyle style, bool apple, bool dark)
	{
		var p = LinkPalette.For(style, apple, dark);

		AtLeast(4.5, p.TextPrimary, p.PageBackground);
		AtLeast(4.5, p.TextPrimary, p.Surface);
		AtLeast(4.5, p.TextSecondary, p.PageBackground);
	}

	[Theory]
	[MemberData(nameof(Every))]
	public void Labels_can_be_read_on_their_fills(LinkStyle style, bool apple, bool dark)
	{
		var p = LinkPalette.For(style, apple, dark);

		AtLeast(4.5, p.OnPrimary, p.Primary);
		AtLeast(4.5, p.OnFab, p.Fab);
	}

	[Theory]
	[MemberData(nameof(Every))]
	public void Primary_stands_off_the_page(LinkStyle style, bool apple, bool dark)
	{
		var p = LinkPalette.For(style, apple, dark);

		// Quiet buttons set their label in Primary straight on the page,
		// so this is text, not just a mark.
		AtLeast(4.5, p.Primary, p.PageBackground);
	}

	[Fact]
	public void A_handset_that_has_never_chosen_keeps_the_blue()
	{
		Assert.Equal(LinkStyle.LinkBlue, default(LinkStyle));
	}

	[Fact]
	public void The_two_styles_differ_on_every_platform()
	{
		foreach (var apple in new[] { true, false })
		{
			Assert.NotEqual(
				LinkPalette.For(LinkStyle.LinkBlue, apple, dark: false).Primary,
				LinkPalette.For(LinkStyle.IntergroupGreen, apple, dark: false).Primary);
		}
	}

	private static void AtLeast(double ratio, string foreground, string background)
	{
		var actual = Contrast(foreground, background);

		Assert.True(actual >= ratio, $"{foreground} on {background} is {actual:0.00}:1, under {ratio}:1");
	}

	private static double Contrast(string a, string b)
	{
		var (la, lb) = (Luminance(a), Luminance(b));

		return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
	}

	private static double Luminance(string hex)
	{
		double Channel(int offset)
		{
			var c = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

			return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
		}

		return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
	}
}
