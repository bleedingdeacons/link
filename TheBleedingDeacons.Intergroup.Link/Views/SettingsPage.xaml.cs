using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class SettingsPage : ContentPage
{
	private readonly SettingsViewModel _viewModel;

	public SettingsPage(SettingsViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.LoadAsync();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		// Neither the share sheet nor the picker takes this page off screen,
		// and both come before or after a passphrase is on it, not during.
		_viewModel.CancelBackup();
	}
}
