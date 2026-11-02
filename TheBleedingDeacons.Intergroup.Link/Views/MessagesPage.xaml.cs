using System.Windows.Input;
using CommunityToolkit.Maui.Core;
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

		_viewModel.ReadPreferences();

		await _viewModel.RefreshAsync();
	}

	/// <summary>
	/// Leaving the list stops choosing. Coming back to checkboxes ticked
	/// before going to Settings would be a delete the member had forgotten
	/// they started.
	/// </summary>
	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		_viewModel.EndSelecting();
	}

	/// <summary>
	/// Back closes an open menu first, then stops choosing what to delete,
	/// and only then leaves. Without this the back gesture would leave the
	/// app with the menu still drawn, and it would still be open when the
	/// member came back.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
		if (MoreMenu.IsVisible)
		{
			ShowMenu(false);
			return true;
		}

		if (_viewModel.Selecting)
		{
			_viewModel.EndSelecting();
			return true;
		}

		return base.OnBackButtonPressed();
	}

	private static async void OnComposeTapped(object? sender, TappedEventArgs e) =>
		await Shell.Current.GoToAsync("compose");

	/// <summary>
	/// A tap on a row: open it, or tick it while choosing. The sender is the
	/// row's Grid, which the TouchBehavior passes as itself; see the comment
	/// on it in MessagesPage.xaml for why this is not a command binding.
	/// </summary>
	private async void OnRowTapped(object? sender, TouchGestureCompletedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: ConversationRow row })
		{
			await _viewModel.OpenAsync(row);
		}
	}

	/// <summary>
	/// The rows' LongPressCommand, which does nothing: its presence is what
	/// makes the toolkit swallow the tap raised when the finger lifts after
	/// a long press. The long press itself is handled by
	/// <see cref="OnRowLongPressed"/>. See the comment on the TouchBehavior
	/// in MessagesPage.xaml.
	/// </summary>
	public static ICommand SwallowsTheTapAfterALongPress { get; } = new Command(() => { });

	/// <summary>A long press on a row: start choosing, with that row ticked.</summary>
	private void OnRowLongPressed(object? sender, LongPressCompletedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: ConversationRow row })
		{
			_viewModel.Select(row);
		}
	}

	/// <summary>
	/// Ask, then delete this phone's copy of every message in every ticked
	/// conversation. Nothing ticked, nothing asked.
	/// </summary>
	/// <remarks>
	/// The confirmation says what it does not do, as the conversation
	/// screen's Delete does; see ConversationPage.OnDeleteClicked. Still
	/// choosing after "Keep them", so a member who wanted to change what was
	/// ticked has not lost the ticks.
	/// </remarks>
	private async void OnDeleteTapped(object? sender, TappedEventArgs e)
	{
		var count = _viewModel.CheckedCount;

		if (count == 0)
		{
			return;
		}

		var confirmed = await DisplayAlertAsync(
			count == 1 ? "Delete this conversation?" : $"Delete {count} conversations?",
			"This deletes every message in " + (count == 1 ? "it" : "them") + " from this phone. "
				+ "They will not come back unless Link is uninstalled and installed again. "
				+ "It does not unsend anything — everyone else still has their copy, and the intergroup keeps its own record.",
			"Delete",
			count == 1 ? "Keep it" : "Keep them");

		if (!confirmed)
		{
			return;
		}

		await _viewModel.DeleteCheckedAsync();
	}

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
