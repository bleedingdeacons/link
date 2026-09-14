using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// The message list.
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

	public MessagesViewModel(
		IMessageService messages,
		IMessageHistory history,
		IUiDispatcher dispatcher,
		IArrivalSound sound)
	{
		_messages = messages;
		_history = history;
		_dispatcher = dispatcher;
		_sound = sound;

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

	public ObservableCollection<LinkMessage> Messages { get; } = [];

	/// <summary>What this member has sent from this phone, newest first, with its ticks.</summary>
	public ObservableCollection<SentMessage> Sent { get; } = [];

	/// <summary>
	/// Which half of the switch is showing. The inbox is the default and
	/// the app opens on it, because an arrival is why it is usually opened.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowingInbox))]
	[NotifyPropertyChangedFor(nameof(IsEmpty))]
	[NotifyPropertyChangedFor(nameof(IsSentEmpty))]
	private bool _showingSent;

	public bool ShowingInbox => !ShowingSent;

	[ObservableProperty]
	private bool _busy;

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

	public bool IsEmpty => Loaded && Messages.Count == 0;

	public bool IsSentEmpty => Loaded && Sent.Count == 0;

	[RelayCommand]
	private void ShowInbox() => ShowingSent = false;

	[RelayCommand]
	private void ShowSent() => ShowingSent = true;

	/// <summary>
	/// Open a sent message: what was written, who to, and how far it has
	/// got. By id, for the same reason <see cref="OpenAsync"/> is.
	/// </summary>
	[RelayCommand]
	public async Task OpenSentAsync(SentMessage? message)
	{
		if (message is null)
		{
			return;
		}

		var route = string.Create(CultureInfo.InvariantCulture, $"message?id={message.Id}&sent=true");

		await Shell.Current.GoToAsync(route).ConfigureAwait(true);
	}

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
	/// Open a message: mark it read, then show it.
	///
	/// <para>Reading is the point, and until there was a screen for it
	/// this did only the marking — so a body longer than the three lines
	/// the row shows could not be read at all. See
	/// <see cref="MessageViewModel"/>.</para>
	///
	/// <para>The list is not reloaded after marking: the record is
	/// replaced in place so the row stops being bold without the view
	/// jumping back to the top, which is what a full reload would do to
	/// somebody halfway down.</para>
	/// </summary>
	[RelayCommand]
	public async Task OpenAsync(LinkMessage? message)
	{
		if (message is null)
		{
			return;
		}

		// Only when it is new. The navigation below happens either way —
		// an already-read message is still one somebody wants to open, and
		// an early return here is what used to make the second tap on a
		// message do nothing at all.
		if (!message.IsRead)
		{
			await _messages.MarkReadAsync(message.Id).ConfigureAwait(true);

			var index = Messages.IndexOf(message);
			if (index >= 0)
			{
				Messages[index] = message with { ReadAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
			}
		}

		// By id rather than by handing the record over: the page is
		// recreated if Android reclaims the process, and an id survives
		// that where an object does not.
		var route = string.Create(CultureInfo.InvariantCulture, $"message?id={message.Id}");

		await Shell.Current.GoToAsync(route).ConfigureAwait(true);
	}

	internal async Task LoadAsync()
	{
		var held = await _history.AllAsync().ConfigureAwait(true);

		Messages.Clear();

		foreach (var message in held)
		{
			Messages.Add(message);
		}

		var sent = await _history.SentAsync().ConfigureAwait(true);

		Sent.Clear();

		foreach (var message in sent)
		{
			Sent.Add(message);
		}

		Loaded = true;
		OnPropertyChanged(nameof(IsEmpty));
		OnPropertyChanged(nameof(IsSentEmpty));
	}
}
