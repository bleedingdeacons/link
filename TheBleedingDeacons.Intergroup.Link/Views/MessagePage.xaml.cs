using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class MessagePage : ContentPage
{
	private readonly MessageViewModel _viewModel;

	public MessagePage(MessageViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Load on appearing rather than in the constructor.
	/// </summary>
	/// <remarks>
	/// The id arrives through <c>ApplyQueryAttributes</c>, which Shell
	/// calls after the page is constructed and before it appears — so a
	/// constructor load would run with no id and find nothing.
	/// </remarks>
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.LoadAsync();
	}
}
