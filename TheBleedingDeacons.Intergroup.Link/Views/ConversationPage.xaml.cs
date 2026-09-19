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
}
