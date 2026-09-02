using System.Collections.ObjectModel;
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

	public MessagesViewModel(IMessageService messages, IMessageHistory history, IUiDispatcher dispatcher)
	{
		_messages = messages;
		_history = history;
		_dispatcher = dispatcher;

		// A pushed message announces itself; see MessageReceived. The
		// handler arrives on whichever thread the push service used, so it
		// hops to the UI before touching an ObservableCollection.
		//
		// WeakReferenceMessenger holds this only weakly, and this view model
		// is a singleton for the app's lifetime, so there is nothing to
		// unregister and no leak to create by not doing so.
		WeakReferenceMessenger.Default.Register<MessageReceived>(this, (_, _) =>
			_dispatcher.Invoke(() => _ = LoadAsync()));
	}

	public ObservableCollection<LinkMessage> Messages { get; } = [];

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

	public bool IsEmpty => Loaded && Messages.Count == 0;

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

			Offline = !result.Succeeded;
			KeyFault = result.KeyFault;

			if (result.Received > 0)
			{
				await LoadAsync().ConfigureAwait(true);
			}
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// Mark a message read, here and — if it can be reached — on the
	/// server.
	///
	/// <para>The list is not reloaded afterwards: the record is replaced
	/// in place so the row stops being bold without the view jumping back
	/// to the top, which is what a full reload would do to somebody
	/// halfway down.</para>
	/// </summary>
	[RelayCommand]
	public async Task OpenAsync(LinkMessage? message)
	{
		if (message is null || message.IsRead)
		{
			return;
		}

		await _messages.MarkReadAsync(message.Id).ConfigureAwait(true);

		var index = Messages.IndexOf(message);
		if (index >= 0)
		{
			Messages[index] = message with { ReadAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
		}
	}

	internal async Task LoadAsync()
	{
		var held = await _history.AllAsync().ConfigureAwait(true);

		Messages.Clear();

		foreach (var message in held)
		{
			Messages.Add(message);
		}

		Loaded = true;
		OnPropertyChanged(nameof(IsEmpty));
	}
}
