using Microsoft.Maui.Controls.Shapes;
using TheBleedingDeacons.Intergroup.Link.Models;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace TheBleedingDeacons.Intergroup.Link.Views;

/// <summary>
/// The ticks on a sent message: one for sent, two grey for received, two
/// blue for read.
///
/// <para><b>The convention most members already read without
/// thinking</b>, which is the whole reason for choosing it over words: a
/// Sent list is scanned, and a shape is recognised before a word is
/// read.</para>
///
/// <para><b>Drawn as Paths, not as a font glyph.</b> Link ships no icon
/// font, and whether a given Android handset's fallback font has U+2713 is
/// not something to find out from a member's screenshot. The compose
/// button's paper plane is a Path for the same reason.</para>
///
/// <para><b>Blue is the unread strip's blue</b>, on purpose: in both lists
/// that colour means "this is about reading". Grey is the strip's grey
/// after reading. A member who cannot tell the two apart still has the
/// count of ticks, and the opened message says it in words.</para>
/// </summary>
public sealed class ReceiptTicks : ContentView
{
	public static readonly BindableProperty StateProperty = BindableProperty.Create(
		nameof(State),
		typeof(ReceiptState),
		typeof(ReceiptTicks),
		ReceiptState.Sent,
		propertyChanged: static (bindable, _, _) => ((ReceiptTicks)bindable).Redraw());

	private const string Tick = "M1,5.5 L4.5,9 L11,1.5";

	private readonly Path _first = NewTick();
	private readonly Path _second = NewTick();

	public ReceiptTicks()
	{
		// The second tick overlaps the first by more than half, as the
		// convention draws it: two ticks read as one mark, not as two
		// separate glyphs side by side.
		_second.TranslationX = 5;

		Content = new Grid
		{
			WidthRequest = 18,
			HeightRequest = 12,
			HorizontalOptions = LayoutOptions.End,
			VerticalOptions = LayoutOptions.Center,
			Children = { _first, _second },
		};

		Redraw();
	}

	public ReceiptState State
	{
		get => (ReceiptState)GetValue(StateProperty);
		set => SetValue(StateProperty, value);
	}

	private static Path NewTick() => new()
	{
		Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(Tick)!,
		StrokeThickness = 1.8,
		StrokeLineCap = PenLineCap.Round,
		StrokeLineJoin = PenLineJoin.Round,
		HorizontalOptions = LayoutOptions.Start,
		VerticalOptions = LayoutOptions.Center,
		WidthRequest = 13,
		HeightRequest = 11,
	};

	private void Redraw()
	{
		_second.IsVisible = State != ReceiptState.Sent;

		var read = State == ReceiptState.Read;

		foreach (var tick in new[] { _first, _second })
		{
			tick.SetAppTheme<Brush>(
				Shape.StrokeProperty,
				new SolidColorBrush(Colour(read ? "Primary" : "Gray500", read ? Color.FromArgb("#3977C3") : Color.FromArgb("#6E6E6E"))),
				new SolidColorBrush(Colour(read ? "PrimaryDark" : "Gray400", read ? Color.FromArgb("#A9C8E8") : Color.FromArgb("#919191"))));
		}

		// Said to a screen reader in words, since a shape says nothing.
		SemanticProperties.SetDescription(this, State switch
		{
			ReceiptState.Read => "Read",
			ReceiptState.Received => "Received",
			_ => "Sent",
		});
	}

	/// <summary>
	/// A colour from the app's own palette, so the ticks follow it if it
	/// changes. The fallback is only for a control built outside the app,
	/// such as a XAML previewer.
	/// </summary>
	private static Color Colour(string key, Color fallback) =>
		Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color colour
			? colour
			: fallback;
}
