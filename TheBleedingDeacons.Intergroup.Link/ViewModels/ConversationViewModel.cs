using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// One conversation, every message in it read in full.
/// </summary>
/// <remarks>
/// <para><b>This is the message view, grown.</b> It replaced a screen
/// that showed one message; that screen existed because the list cut a
/// body off at three lines and there was otherwise nowhere to read the
/// rest. Each message here is drawn exactly as that screen drew one —
/// subject, who, when, a divider, the whole body — one after another,
/// oldest first. See Conversations.feature for which messages belong.</para>
///
/// <para><b>Reply and Forward are on every message</b>, not once for the
/// screen. Which message a reply answers is the whole of the thread model,
/// so the member says which by pressing the button under it.</para>
///
/// <para><b>Looked up by id rather than handed the object.</b> Shell can
/// carry an object through a navigation parameter, but the page is
/// recreated on a process restart — Android will do that to a backgrounded
/// app — and a screen that came back blank after the system reclaimed
/// memory would be a bug nobody could reproduce on demand. An id survives
/// that; the history is local and already decrypted, so the lookup costs
/// nothing.</para>
///
/// <para>Found by any message in it rather than by its root, so a root
/// that changes — an original that turned up after its answer — does not
/// strand a screen that was opened before it did.</para>
/// </remarks>
public sealed partial class ConversationViewModel : ObservableObject, IQueryAttributable
{
	private readonly IMessageHistory _history;
	private readonly IMessageService _messages;

	private long _id;

	public ConversationViewModel(IMessageHistory history, IMessageService messages)
	{
		_history = history;
		_messages = messages;
	}

	public ObservableCollection<ConversationItem> Items { get; } = [];

	/// <summary>
	/// Set when nothing in the conversation is on this phone any more —
	/// the history was cleared, or it aged out — so the screen can say so
	/// instead of showing nothing that looks like a blank message.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Found))]
	private bool _missing;

	/// <summary>
	/// The other half of <see cref="Missing"/>, as a property rather than
	/// an inverting converter: a converter that is not registered fails at
	/// runtime and the build says nothing, where a missing property is a
	/// compile error the XAML compiler catches.
	/// </summary>
	public bool Found => !Missing;

	/// <summary>The id arrives as a string from the route, so it is parsed rather than cast.</summary>
	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (query.TryGetValue("id", out var raw)
			&& long.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
		{
			_id = id;
		}
	}

	[RelayCommand]
	public async Task LoadAsync()
	{
		var received = await _history.AllAsync().ConfigureAwait(true);
		var sent = await _history.SentAsync().ConfigureAwait(true);

		var conversation = Conversations.Containing(Conversations.Build(received, sent), _id);

		Items.Clear();

		if (conversation is null)
		{
			Missing = true;
			return;
		}

		Missing = false;

		foreach (var entry in conversation.Messages)
		{
			Items.Add(new ConversationItem(
				entry,
				isRoot: entry.Id == conversation.Root.Id,
				answer: Conversations.RepliedTo(entry.Id, sent)));
		}

		// Opening the conversation is reading it. After drawing, so the
		// screen does not wait on the network, and the strip on the list
		// is gone by the time the member goes back to it.
		foreach (var entry in conversation.Messages.Where(m => !m.IsRead))
		{
			await _messages.MarkReadAsync(entry.Id).ConfigureAwait(true);
		}
	}

	/// <summary>
	/// Open Compose answering this message.
	/// </summary>
	/// <remarks>
	/// The subject is escaped because it is member-typed text going into a
	/// query string, and an unescaped <c>&amp;</c> in a subject would
	/// truncate it and take the rest of the parameters with it.
	///
	/// <para><c>to</c> is who the reply starts addressed to, as a member id;
	/// Compose chooses them once the directory is in. See
	/// <see cref="Replying"/>.</para>
	/// </remarks>
	public static Task ReplyAsync(ConversationItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		var route = string.Create(
			CultureInfo.InvariantCulture,
			$"compose?replyTo={item.Entry.Id}&to={Replying.AddressFor(item.Entry)}&subject={Uri.EscapeDataString(item.Subject)}");

		return Shell.Current.GoToAsync(route);
	}

	/// <summary>
	/// Open Compose passing this message on. By id: Compose looks the
	/// original up and quotes it, so no body travels through a route.
	/// </summary>
	public static Task ForwardAsync(ConversationItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		return Shell.Current.GoToAsync(
			string.Create(CultureInfo.InvariantCulture, $"compose?forward={item.Entry.Id}"));
	}
}
