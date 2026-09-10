using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// One message, read in full.
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> Until it did, a message could not
/// be read. The list rendered the body with <c>MaxLines="3"</c> and
/// truncation, tapping a row only marked it read, and there was nowhere
/// else to go — so anything longer than three lines was permanently cut
/// off in the only app that displays it. Fellowship stores the whole body
/// and the handset holds it decrypted; nothing was missing but a
/// screen.</para>
///
/// <para>It also makes <b>reply</b> reachable.
/// <c>ComposeViewModel.ApplyQueryAttributes</c> has always understood
/// <c>replyTo</c> and <c>subject</c>, and Fellowship has always accepted a
/// reply id, but nothing in the app ever navigated with them — the whole
/// path was dead code. Reply belongs here rather than on the list, because
/// replying to something you have not opened is not a thing anybody
/// does.</para>
///
/// <para><b>Looked up by id rather than handed the object.</b> Shell can
/// carry an object through a navigation parameter, but the page is
/// recreated on a process restart — Android will do that to a backgrounded
/// app — and a detail screen that came back blank after the system
/// reclaimed memory would be a bug nobody could reproduce on demand. An id
/// survives that; the history is local and already decrypted, so the
/// lookup costs nothing.</para>
/// </remarks>
public sealed partial class MessageViewModel : ObservableObject, IQueryAttributable
{
	private readonly IMessageHistory _history;

	public MessageViewModel(IMessageHistory history)
	{
		_history = history;
	}

	[ObservableProperty]
	private string _subject = string.Empty;

	[ObservableProperty]
	private string _sender = string.Empty;

	[ObservableProperty]
	private string _sent = string.Empty;

	[ObservableProperty]
	private string _body = string.Empty;

	/// <summary>
	/// Set when the message could not be found — the history was cleared,
	/// or it aged out — so the screen can say so instead of showing an
	/// empty card that looks like a blank message.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Found))]
	private bool _missing;

	/// <summary>
	/// The other half of <see cref="Missing"/>, as a property rather than
	/// an inverting converter in the XAML. Hand made the same choice for
	/// the same reason: a converter that is not registered fails at
	/// runtime and the build says nothing, where a missing property is a
	/// compile error the XAML compiler catches.
	/// </summary>
	public bool Found => !Missing;

	private long _id;

	/// <summary>
	/// The id arrives as a string from the route, whatever it was on the
	/// way in, so it is parsed rather than cast.
	/// </summary>
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
		var held = await _history.AllAsync().ConfigureAwait(true);
		var message = held.FirstOrDefault(m => m.Id == _id);

		if (message is null)
		{
			Missing = true;
			return;
		}

		Missing = false;
		Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;
		Sender = message.Sender;
		Body = message.Body;

		// Local time and spelled out. A list can afford "3 Sep" because it
		// is scanned; the message somebody opened is the one they may want
		// to quote a time from.
		Sent = message.Sent.ToLocalTime().ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);
	}

	/// <summary>
	/// Open Compose with this message as the reply target.
	/// </summary>
	/// <remarks>
	/// The subject is escaped because it is member-typed text going into a
	/// query string, and an unescaped <c>&amp;</c> in a subject would
	/// truncate it and take the rest of the parameters with it.
	/// </remarks>
	[RelayCommand]
	public async Task ReplyAsync()
	{
		if (Missing || _id <= 0)
		{
			return;
		}

		var route = string.Create(
			CultureInfo.InvariantCulture,
			$"compose?replyTo={_id}&subject={Uri.EscapeDataString(Subject)}");

		await Shell.Current.GoToAsync(route).ConfigureAwait(true);
	}
}
