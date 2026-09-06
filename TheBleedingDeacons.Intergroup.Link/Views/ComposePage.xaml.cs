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

	/// <summary>
	/// The scope radios, written one-way in XAML and set from here.
	///
	/// <para>A two-way binding on <c>IsChecked</c> would fight itself:
	/// selecting one radio unchecks the other, and that uncheck fires the
	/// same handler. Acting only on the one that became checked is what
	/// keeps a single choice from being two writes in an order nothing
	/// controls. Hand does it this way for the same reason.</para>
	/// </summary>
	private void OnMemberScopeChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e is not null && e.Value)
		{
			_viewModel.RecipientMode = 0;
		}
	}

	private void OnCommitteeScopeChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e is not null && e.Value)
		{
			_viewModel.RecipientMode = 1;
		}
	}
}
