using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class MessagesPage : ContentPage
{
	private readonly MessagesViewModel _viewModel;

	public MessagesPage(MessagesViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Refresh every time the page is shown.
	///
	/// <para>Not only on first load: the app is most often opened *because*
	/// a notification arrived, and a list that only synced once would show
	/// the member everything except the message they came to read. The
	/// first half of the refresh reads the local history and is instant,
	/// so this costs nothing visible.</para>
	/// </summary>
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.RefreshAsync();
	}

	/// <summary>
	/// Back closes an open menu first. Without this the back gesture would
	/// leave the app with the menu still drawn, and it would still be open
	/// when the member came back.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
		if (MoreMenu.IsVisible)
		{
			ShowMenu(false);
			return true;
		}

		return base.OnBackButtonPressed();
	}

	private static async void OnComposeTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("compose");

	private void OnMoreClicked(object? sender, EventArgs e) =>
		ShowMenu(!MoreMenu.IsVisible);

	private void OnMenuScrimTapped(object? sender, TappedEventArgs e) =>
		ShowMenu(false);

	private async void OnSettingsTapped(object? sender, TappedEventArgs e)
	{
		ShowMenu(false);

		await Shell.Current.GoToAsync("settings");
	}

	/// <summary>
	/// Open or close the ⋮ dropdown.
	///
	/// <para>The top margin is worked out when the menu opens, from where the
	/// button actually is. A fixed number in XAML would be right on one
	/// handset and wrong on the next: the header's height moves with the
	/// member's font scale.</para>
	/// </summary>
	private void ShowMenu(bool show)
	{
		if (show)
		{
			MoreMenu.Margin = new Thickness(0, Header.Y + MoreButton.Y + MoreButton.Height, 8, 0);
		}

		MenuScrim.IsVisible = show;
		MoreMenu.IsVisible = show;
	}
}
