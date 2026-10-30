using System.ComponentModel;
using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class SignInPage : ContentPage
{
	private readonly SignInViewModel _viewModel;

	public SignInPage(SignInViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Listening only while the page is on screen, so the view model does
	/// not keep a page alive that the shell has already let go of.
	/// </summary>
	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.PropertyChanged += OnViewModelChanged;
	}

	protected override void OnDisappearing()
	{
		_viewModel.PropertyChanged -= OnViewModelChanged;
		base.OnDisappearing();
	}

	/// <summary>
	/// Bring a new message into view, wherever the member was on the page.
	///
	/// <para>The messages sit above the buttons, which is where a
	/// sign-in starts. The set-a-password section is at the foot, though,
	/// and its answers land up there too; without this they would be as
	/// far out of sight as the sign-in messages used to be.</para>
	/// </summary>
	private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		var appeared = e.PropertyName switch
		{
			nameof(SignInViewModel.HasError) => _viewModel.HasError,
			nameof(SignInViewModel.HasNotice) => _viewModel.HasNotice,
			_ => false,
		};

		if (appeared)
		{
			Dispatcher.Dispatch(async () =>
				await Scroller.ScrollToAsync(Messages, ScrollToPosition.MakeVisible, animated: true).ConfigureAwait(true));
		}
	}
}
