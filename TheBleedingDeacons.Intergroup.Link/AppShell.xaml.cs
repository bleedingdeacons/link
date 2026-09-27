using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Views;

namespace TheBleedingDeacons.Intergroup.Link;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Compose is pushed rather than being top-level: it is always
		// entered from somewhere — a new message, or a reply to one — and
		// a top-level page would give it a third meaning ("compose to
		// nobody in particular") that the send route refuses anyway.
		Routing.RegisterRoute("compose", typeof(ComposePage));

		// Reading one conversation, every message in it in full. Pushed
		// for the same reason Compose is: it is always entered from the
		// list, and both platforms already know how to undo a push - the
		// iOS back chevron and edge swipe, the Android back gesture - so
		// nothing here draws a close button.
		Routing.RegisterRoute("conversation", typeof(ConversationPage));

		// Settings is pushed from the message list's header, as Hand's is
		// from its duty screen. It was a tab; a tab is somewhere the app
		// can be left sitting, and the message list is where it belongs.
		Routing.RegisterRoute("settings", typeof(SettingsPage));

		// Decided once at startup, then again whenever sign-in state
		// changes. Reading it here rather than in a view model because it
		// is a question about the shell, and a view model that reached up
		// to reorganise its own container would be the wrong shape.
		_ = RefreshAsync();
	}

	/// <summary>
	/// Show the message list or the sign-in page, depending on whether
	/// this handset is enrolled.
	/// </summary>
	public async Task RefreshAsync()
	{
		var session = await LinkServices.Sessions.LoadAsync().ConfigureAwait(false);
		var signedIn = session is not null && session.IsSignedIn;

		// GoToAsync rather than setting CurrentItem: it works the same on a
		// fresh launch and on a sign-out from a pushed Settings page, which
		// setting CurrentItem does not. An absolute route also clears that
		// pushed page off the stack, so a signed-out member cannot back
		// into it.
		Dispatcher.Dispatch(() => _ = GoToAsync(signedIn ? "//messages" : "//signin"));

		// The same moments decide where logs go: asked for once signed in,
		// forgotten once not. Not awaited, so nobody waits on a logging
		// request to see their messages.
		_ = RefreshLoggingAsync();
	}

	private static async Task RefreshLoggingAsync()
	{
		try
		{
			var logging = IPlatformApplication.Current?.Services.GetService<RemoteLogging>();
			if (logging is not null)
			{
				await logging.RefreshAsync().ConfigureAwait(false);
			}
		}
#pragma warning disable CA1031 // Fire and forget: there is nobody to throw to.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Log settings could not be refreshed from the intergroup");
		}
	}
}
