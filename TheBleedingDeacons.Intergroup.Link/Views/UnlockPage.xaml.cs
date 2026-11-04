using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class UnlockPage : ContentPage
{
	private readonly UnlockViewModel _viewModel;

	public UnlockPage(UnlockViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Raise the sheet as soon as the page is up, so coming back in is one
	/// touch of the sensor rather than a tap and then a touch.
	///
	/// <para>Dispatched rather than called inline: on a cold start the page
	/// appears while the activity is still finishing its own start, and
	/// Android's prompt wants a window that is already showing.</para>
	/// </summary>
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.LoadAsync();

		Dispatcher.Dispatch(() =>
		{
			if (_viewModel.UnlockCommand.CanExecute(null))
			{
				_viewModel.UnlockCommand.Execute(null);
			}
		});
	}
}
