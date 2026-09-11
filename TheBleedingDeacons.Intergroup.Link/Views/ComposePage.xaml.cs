using TheBleedingDeacons.Intergroup.Link.Models;
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
	/// A tap on a chip's cross, and a tap on a row of the list.
	/// </summary>
	/// <remarks>
	/// <para>Handled here rather than bound to the view model's commands,
	/// and the reason is the compiler rather than taste. This project
	/// compiles its XAML bindings, and a compiled binding resolves its
	/// Path against the <c>x:DataType</c> in scope — never against
	/// <c>Source</c>. So reaching the page's BindingContext from inside a
	/// DataTemplate whose type is <see cref="Recipient"/> is XC0045, as is
	/// reading <c>SelectedItem</c> off a CollectionView from a page typed
	/// to the view model.</para>
	///
	/// <para>Worth knowing where that was found: a local Debug build does
	/// not compile bindings and ran both happily by reflection, chips and
	/// all, on a handset. CI compiles them and refused.</para>
	/// </remarks>
	private void OnDropTapped(object? sender, TappedEventArgs e)
	{
		if (sender is Element { BindingContext: Recipient recipient })
		{
			_viewModel.DropCommand.Execute(recipient);
		}
	}

	private void OnCandidateSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e?.CurrentSelection.FirstOrDefault() is not Recipient recipient)
		{
			return;
		}

		_viewModel.ChooseCommand.Execute(recipient);

		// Choosing takes the row out of the list, so the control has
		// nothing left selected; saying so explicitly keeps a stale
		// selection from surviving a filter that puts the row back.
		if (sender is CollectionView list)
		{
			list.SelectedItem = null;
		}
	}
}
