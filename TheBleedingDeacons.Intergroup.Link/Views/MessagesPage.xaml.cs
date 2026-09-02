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

	private static async void OnComposeClicked(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync("compose");
}
