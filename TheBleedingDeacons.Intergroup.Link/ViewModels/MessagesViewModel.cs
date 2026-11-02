using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The message list: one row per conversation.
///
/// <para><b>One list, not two.</b> It was an Inbox and a Sent list behind
/// a switch, which put an answer and the message it answered on different
/// screens. What was received and what was sent now sit together, grouped
/// by what they answer — see <see cref="Models.Conversations"/> and
/// Conversations.feature.</para>
///
/// <para><b>It shows the local history, not the server's answer.</b> The
/// list is filled from <see cref="IMessageHistory"/> every time, and a
/// sync is something that happens beside it. So the app opens instantly
/// with what it already has, works in a tunnel, and does not blank itself
/// when a poll fails — which is the behaviour that separates a messaging
/// app from a web page with an app icon.</para>
/// </summary>
public sealed partial class MessagesViewModel : ObservableObject
{
	private readonly IMessageService _messages;
	private readonly IMessageHistory _history;
	private readonly IUiDispatcher _dispatcher;
	private readonly IArrivalSound _sound;
	private readonly IPullToRefresh _pull;

	public MessagesViewModel(
		IMessageService messages,
		IMessageHistory history,
		IUiDispatcher dispatcher,
		IArrivalSound sound,
		IPullToRefresh pull)
	{
		_messages = messages;
		_history = history;
		_dispatcher = dispatcher;
		_sound = sound;
		_pull = pull;
		_canPull = pull.Enabled;

		// A pushed message announces itself; see MessageReceived. The
		// handler arrives on whichever thread the push service used, so it
		// hops to the UI before touching an ObservableCollection.
		//
		// WeakReferenceMessenger holds this only weakly, and this view model
		// is a singleton for the app's lifetime, so there is nothing to
		// unregister and no leak to create by not doing so.
		WeakReferenceMessenger.Default.Register<MessageReceived>(this, (_, _) =>
			_dispatcher.Invoke(() =>
			{
				// A push that arrives while the app is open is announced by
				// nothing else: the platform's notification is for a message
				// that lands with Link closed. Without this the list simply
				// grows a row in silence.
				_sound.Play();

				_ = LoadAsync();
			}));

		// And the other thing the sync loop can discover: that this phone
		// is no longer signed in. It arrives the same way and for the same
		// reason — MessageService has no view model and no navigation, and
		// should not learn about either.
		WeakReferenceMessenger.Default.Register<AuthenticationLost>(this, (_, lost) =>
			_dispatcher.Invoke(() =>
			{
				SignedOutReason = lost.Reason;

				// Not offline. The intergroup answered; it said no. Showing
				// "could not be reached" here is the bug this whole path
				// exists to fix.
				Offline = false;
			}));

		// A sync that learned a sent message moved on. Redrawn from the
		// history, like an arrival, so the ticks change in front of a
		// sender who is watching for them.
		WeakReferenceMessenger.Default.Register<ReceiptsChanged>(this, (_, _) =>
			_dispatcher.Invoke(() => _ = LoadAsync()));
	}

	/// <summary>Every conversation, the one with the newest message first.</summary>
	public ObservableCollection<ConversationRow> Conversations { get; } = [];

	/// <summary>
	/// True while the list is choosing conversations to delete: a long
	/// press starts it, every row shows a checkbox, and the compose button
	/// becomes Delete. Back ends it; see <c>MessagesPage.OnBackButtonPressed</c>.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Browsing))]
	private bool _selecting;

	/// <summary>The opposite of <see cref="Selecting"/>, for the compose button to bind to.</summary>
	public bool Browsing => !Selecting;

	public int CheckedCount => Conversations.Count(row => row.IsChecked);

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PullSpinning))]
	private bool _busy;

	/// <summary>
	/// Whether the list can be pulled down to check for new messages. Read
	/// from <see cref="IPullToRefresh"/> each time the list is shown, so
	/// turning it on in Settings takes effect on the way back. See
	/// <see cref="ReadPreferences"/>.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PullSpinning))]
	[NotifyPropertyChangedFor(nameof(CannotPull))]
	private bool _canPull;

	/// <summary>The opposite, for the empty list's wording to bind to, as <see cref="Browsing"/> is.</summary>
	public bool CannotPull => !CanPull;

	/// <summary>
	/// The pull's spinner. Only while the pull is on: with it off, the
	/// check made each time the list is shown happens without one, rather
	/// than a spinner dropping from the top of a list that cannot be
	/// pulled.
	/// </summary>
	public bool PullSpinning => CanPull && Busy;

	/// <summary>Pick up anything changed in Settings since the list was last shown.</summary>
	public void ReadPreferences() => CanPull = _pull.Enabled;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsEmpty))]
	private bool _loaded;

	/// <summary>
	/// Set when at least one message arrived that this handset could not
	/// open.
	///
	/// <para>Worth a banner rather than silence: the member is looking at
	/// a list that is missing something, and without being told they would
	/// conclude the message was never sent. The recovery is on the
	/// Settings screen.</para>
	/// </summary>
	[ObservableProperty]
	private bool _keyFault;

	/// <summary>
	/// True when the last sync could not reach the intergroup. Shown
	/// quietly — the list is still usable, it is just not current.
	/// </summary>
	[ObservableProperty]
	private bool _offline;

	/// <summary>
	/// Why this phone was signed out, or empty while it still is signed
	/// in.
	///
	/// <para>A banner rather than a redirect. The member keeps their
	/// messages when authorisation is lost — see <c>AuthenticationLost</c>
	/// — so throwing them at a sign-in screen would take away the one
	/// thing still working while they read what happened.</para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(SignedOut))]
	private string _signedOutReason = string.Empty;

	public bool IsEmpty => Loaded && Conversations.Count == 0;

	public bool SignedOut => SignedOutReason.Length > 0;

	/// <summary>
	/// Fill the list from what is held, then sync, then fill it again.
	///
	/// <para>Two loads rather than one, deliberately. The first is
	/// instant and works offline; the second is what makes new messages
	/// appear. Doing only the second would mean a blank screen for as long
	/// as the network takes.</para>
	/// </summary>
	[RelayCommand]
	public async Task RefreshAsync()
	{
		await LoadAsync().ConfigureAwait(true);

		Busy = true;

		try
		{
			var result = await _messages.SyncAsync().ConfigureAwait(true);

			// Only a sync that never arrived is "offline". A refusal has
			// already announced itself through AuthenticationLost, and
			// saying both would have the screen blame the network for a
			// decision the intergroup made.
			Offline = result.Failure == FellowshipFailure.Network;
			KeyFault = result.KeyFault;

			// Receipts can move on a sync that brought nothing in; they
			// announce themselves through ReceiptsChanged, which reloads.
			if (result.Received > 0)
			{
				// The poll's arrival, which is the one that matters on a
				// handset with no push configured -- there, every message
				// arrives this way.
				_sound.Play();

				await LoadAsync().ConfigureAwait(true);
			}
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// Open a conversation, which marks what is unread in it as read.
	///
	/// <para>By id rather than by handing the record over: the page is
	/// recreated if Android reclaims the process, and an id survives that
	/// where an object does not. The newest message's id rather than the
	/// root's, so a conversation whose root changes while it is open — an
	/// original arriving after its answer — is still found.</para>
	/// </summary>
	/// <remarks>
	/// While <see cref="Selecting"/>, a tap ticks or unticks the row
	/// instead. The whole row is the target either way, checkbox included.
	/// </remarks>
	[RelayCommand]
	public async Task OpenAsync(ConversationRow? row)
	{
		if (row is null)
		{
			return;
		}

		if (Selecting)
		{
			row.IsChecked = !row.IsChecked;
			return;
		}

		var route = string.Create(CultureInfo.InvariantCulture, $"conversation?id={row.Conversation.Latest.Id}");

		await Shell.Current.GoToAsync(route).ConfigureAwait(true);
	}

	/// <summary>
	/// A long press: start choosing, with the row pressed already ticked —
	/// it is the one the member had in mind. A long press while already
	/// choosing ticks that row too, rather than doing nothing.
	/// </summary>
	public void Select(ConversationRow? row)
	{
		if (row is null)
		{
			return;
		}

		Selecting = true;
		row.IsChecked = true;
	}

	/// <summary>Stop choosing, and untick everything so the next long press starts clean.</summary>
	public void EndSelecting()
	{
		foreach (var row in Conversations)
		{
			row.IsChecked = false;
		}

		Selecting = false;
	}

	/// <summary>
	/// Delete this phone's copy of every message in every ticked
	/// conversation, then stop choosing and redraw.
	/// </summary>
	/// <remarks>
	/// One message at a time through <see cref="IMessageHistory.DeleteAsync"/>,
	/// the same delete the conversation screen offers, so everything said
	/// there holds here: nothing is unsent, the server is not told, and
	/// what was deleted stays deleted. An answer that arrives later to a
	/// conversation deleted this way starts a conversation of its own.
	/// </remarks>
	public async Task DeleteCheckedAsync()
	{
		var ids = Conversations
			.Where(row => row.IsChecked)
			.SelectMany(row => row.Conversation.Messages)
			.Select(entry => entry.Id)
			.ToList();

		foreach (var id in ids)
		{
			await _history.DeleteAsync(id).ConfigureAwait(true);
		}

		EndSelecting();

		await LoadAsync().ConfigureAwait(true);
	}

	internal async Task LoadAsync()
	{
		var received = await _history.AllAsync().ConfigureAwait(true);
		var sent = await _history.SentAsync().ConfigureAwait(true);
		var deleted = await _history.DeletedAsync().ConfigureAwait(true);

		// A message arriving while the member is choosing redraws the list,
		// and the ticks have to survive that. Carried by message rather
		// than by row, because the rows are new and a conversation's root
		// can change underneath them.
		var ticked = Conversations
			.Where(row => row.IsChecked)
			.SelectMany(row => row.Conversation.Messages)
			.Select(entry => entry.Id)
			.ToHashSet();

		Conversations.Clear();

		foreach (var conversation in Models.Conversations.Build(received, sent, deleted))
		{
			Conversations.Add(new ConversationRow(conversation)
			{
				IsChecked = conversation.Messages.Any(entry => ticked.Contains(entry.Id)),
			});
		}

		Loaded = true;
		OnPropertyChanged(nameof(IsEmpty));
	}
}
