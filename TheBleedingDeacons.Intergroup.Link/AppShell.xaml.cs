using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Views;

namespace TheBleedingDeacons.Intergroup.Link;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Compose is pushed rather than being a tab: it is always entered
		// from somewhere — a new message, or a reply to one — and a tab
		// would give it a third meaning ("compose to nobody in
		// particular") that the send route refuses anyway.
		Routing.RegisterRoute("compose", typeof(ComposePage));

		ShowSignedIn(false);

		// Decided once at startup, then again whenever sign-in state
		// changes. Reading it here rather than in a view model because it
		// is a question about the shell, and a view model that reached up
		// to reorganise its own container would be the wrong shape.
		_ = RefreshAsync();
	}

	/// <summary>
	/// Show the tabs or the sign-in page, depending on whether this
	/// handset is enrolled.
	/// </summary>
	public async Task RefreshAsync()
	{
		var session = await LinkServices.Sessions.LoadAsync().ConfigureAwait(false);
		var signedIn = session is not null && session.IsSignedIn;

		Dispatcher.Dispatch(() =>
		{
			ShowSignedIn(signedIn);

			// GoToAsync rather than setting CurrentItem: it works the same
			// on a fresh launch and on a sign-out from deep inside the
			// tabs, which setting CurrentItem does not.
			_ = GoToAsync(signedIn ? "//messages" : "//signin");
		});
	}

	private void ShowSignedIn(bool signedIn)
	{
		SignIn.IsVisible = !signedIn;
		Tabs.IsVisible = signedIn;
	}
}
