using TheBleedingDeacons.Intergroup.Link.ViewModels;

namespace TheBleedingDeacons.Intergroup.Link.Views;

public partial class ConversationPage : ContentPage
{
	private readonly ConversationViewModel _viewModel;

	public ConversationPage(ConversationViewModel viewModel)
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
	/// constructor load would run with no id and find nothing. Every time,
	/// not once: coming back from sending a reply should show the reply.
	/// </remarks>
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		await _viewModel.LoadAsync();
	}

	private static async void OnReplyClicked(object? sender, EventArgs e)
	{
		if (sender is Element { BindingContext: ConversationItem item })
		{
			await ConversationViewModel.ReplyAsync(item);
		}
	}

	private static async void OnForwardClicked(object? sender, EventArgs e)
	{
		if (sender is Element { BindingContext: ConversationItem item })
		{
			await ConversationViewModel.ForwardAsync(item);
		}
	}

	/// <summary>
	/// Ask, then delete this phone's copy.
	/// </summary>
	/// <remarks>
	/// The confirmation says what it does not do, as Clear messages' does:
	/// "delete" reads to most people as "take it back", and a member who
	/// believed they had recalled something would be worse off than one
	/// who never pressed the button. Back to the list when nothing is left
	/// to show.
	/// </remarks>
	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		if (sender is not Element { BindingContext: ConversationItem item })
		{
			return;
		}

		var confirmed = await DisplayAlertAsync(
			"Delete this message?",
			"This deletes it from this phone, and it will not come back. It does not unsend anything — "
				+ "everyone else still has their copy, and the intergroup keeps its own record.",
			"Delete",
			"Keep it");

		if (!confirmed)
		{
			return;
		}

		if (!await _viewModel.DeleteAsync(item))
		{
			await Shell.Current.GoToAsync("..");
		}
	}
}
