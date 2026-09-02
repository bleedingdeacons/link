using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class ComposePage : ContentPage
{
	private readonly ComposeViewModel _viewModel;

	public ComposePage(ComposeViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Fetch the directory each time this page opens.
	///
	/// <para>Not cached across openings on purpose: a directory is a live
	/// list of who may be messaged, and a stale one shows names that
	/// Fellowship will silently drop from the send. It is one small
	/// request, made only when somebody is actually composing.</para>
	/// </summary>
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.LoadAsync();
	}
}
