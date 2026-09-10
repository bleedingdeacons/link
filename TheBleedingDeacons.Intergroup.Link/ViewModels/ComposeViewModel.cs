using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// Writing a message.
///
/// <para><b>Recipients are picked, never typed.</b> The list comes from
/// Fellowship's directory as names and opaque ids, and what goes back is
/// the ids. So this screen cannot address a message to somebody who is
/// not a member, cannot address one to a typo, and never holds anybody's
/// email address in the first place.</para>
///
/// <para><b>One scope or the other, never both</b>, chosen with a pair of
/// radios before the list rather than inferred from what happens to be
/// selected in two lists at once. Fellowship refuses a message addressed
/// to a committee and to named people together, because the recipient
/// list that would produce cannot be explained back to whoever sent it.
/// This is Hand's arrangement, adopted so a member holding both apps
/// picks a recipient the same way in each.</para>
///
/// <para>The committee scope appears only when the site allows committee
/// sends from the app, which is off by default — Fellowship simply sends
/// an empty committee list, and there is nothing here to hide.</para>
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject, IQueryAttributable
{
	private readonly IMessageService _messages;
	private readonly IFellowshipClient _client;
	private readonly ISessionStore _sessions;

	public ComposeViewModel(IMessageService messages, IFellowshipClient client, ISessionStore sessions)
	{
		_messages = messages;
		_client = client;
		_sessions = sessions;
	}

	/// <summary>
	/// Everyone the directory returned, before <see cref="Search"/> is
	/// applied. Held so filtering is a local operation rather than another
	/// request per keystroke — Link is handed the whole address book in one
	/// response, unlike Hand, whose member list is paged and searched
	/// server-side.
	/// </summary>
	private readonly List<DirectoryMember> _allPeople = [];

	/// <summary>
	/// The names actually on screen: <see cref="_allPeople"/> narrowed by
	/// <see cref="Search"/>.
	/// </summary>
	public ObservableCollection<DirectoryMember> People { get; } = [];

	public ObservableCollection<DirectoryCommittee> Committees { get; } = [];

	/// <summary>
	/// Which of the two recipient lists is in force: 0 a member, 1 a
	/// committee. An int rather than an enum because it is set from two
	/// radio handlers and read by two visibility bindings, and neither
	/// gains anything from a named type.
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private int _recipientMode;

	/// <summary>
	/// The one member this message is for, in member scope.
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private DirectoryMember? _selectedPerson;

	/// <summary>
	/// Narrows the member list as it is typed into. Never sent anywhere.
	/// </summary>
	[ObservableProperty]
	private string _search = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private string _subject = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private string _body = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private DirectoryCommittee? _committee;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	private bool _busy;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string _error = string.Empty;

	/// <summary>
	/// The message being replied to, or 0. Set by navigation.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsReply))]
	private long _replyToId;

	[ObservableProperty]
	private string _replyToSubject = string.Empty;

	public bool HasError => !string.IsNullOrEmpty(Error);

	public bool IsReply => ReplyToId > 0;

	public bool IsMemberMode => RecipientMode == 0;

	public bool IsCommitteeMode => RecipientMode == 1;

	/// <summary>
	/// Whether this site lets the app address a whole committee.
	///
	/// <para>Derived from the directory rather than from a setting the app
	/// holds: Fellowship sends no committees when it will not accept a
	/// committee send, so the two cannot disagree.</para>
	/// </summary>
	public bool CanSendToCommittee => Committees.Count > 0;

	/// <summary>
	/// Fill in the reply target, when Compose was opened from a message.
	/// </summary>
	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (query.TryGetValue("replyTo", out var id) && long.TryParse(id?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		{
			ReplyToId = parsed;
		}

		if (query.TryGetValue("subject", out var subject))
		{
			ReplyToSubject = subject?.ToString() ?? string.Empty;

			// Prefilled, not forced. "Re: …" is what somebody almost
			// always wants and occasionally does not.
			Subject = ReplyToSubject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
				? ReplyToSubject
				: "Re: " + ReplyToSubject;
		}
	}

	[RelayCommand]
	public async Task LoadAsync()
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(true);
		if (session is null || !session.IsSignedIn)
		{
			return;
		}

		var directory = await _client.FetchDirectoryAsync(session.Token).ConfigureAwait(true);

		_allPeople.Clear();
		_allPeople.AddRange(directory.Members);
		ApplySearch();

		Committees.Clear();
		foreach (var committee in directory.Committees)
		{
			Committees.Add(committee);
		}

		OnPropertyChanged(nameof(CanSendToCommittee));

		// A site that sends no committees leaves the committee radio hidden,
		// so a screen sitting in committee scope would show an empty list and
		// no way back to the members. Only possible if the directory changed
		// under a page that was already open, which is exactly when nobody
		// would think to look for it.
		if (!CanSendToCommittee && IsCommitteeMode)
		{
			RecipientMode = 0;
		}
	}

	/// <summary>
	/// Rebuild the visible list from the held one.
	///
	/// <para>Rebuilt rather than filtered in place: a selection that has
	/// just been typed out of view has to be dropped, and doing both in one
	/// place is what keeps <see cref="SelectedPerson"/> from naming
	/// somebody the sender can no longer see.</para>
	/// </summary>
	private void ApplySearch()
	{
		var term = Search.Trim();

		People.Clear();
		foreach (var person in _allPeople)
		{
			// Home group counts as well as the name, now that the row
			// shows one: somebody who knows a member only as "the GSR
			// from Tuesday Bristol" can find them by typing the group,
			// which is a real way people describe each other and was a
			// dead end while only the name matched.
			if (term.Length == 0
				|| person.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
				|| person.HomeGroup.Contains(term, StringComparison.CurrentCultureIgnoreCase))
			{
				People.Add(person);
			}
		}

		if (SelectedPerson is not null && !People.Contains(SelectedPerson))
		{
			SelectedPerson = null;
		}
	}

	partial void OnSearchChanged(string value) => ApplySearch();

	partial void OnRecipientModeChanged(int value)
	{
		OnPropertyChanged(nameof(IsMemberMode));
		OnPropertyChanged(nameof(IsCommitteeMode));

		// Switching scope clears the other side's choice. Leaving both set
		// would let the screen show a committee while the send addressed a
		// member — and Fellowship refuses both at once anyway.
		if (value == 0)
		{
			Committee = null;
		}
		else
		{
			SelectedPerson = null;
		}
	}

	[RelayCommand(CanExecute = nameof(CanSend))]
	private async Task SendAsync()
	{
		Busy = true;
		Error = string.Empty;

		try
		{
			var result = await _messages.SendAsync(new SendRequest
			{
				Subject = Subject,
				Body = Body,
				// A committee and named people are mutually exclusive —
				// Fellowship refuses a request carrying both, because the
				// resulting recipient list cannot be explained back to
				// whoever sent it. The scope radios enforce the same thing,
				// so this is belt and braces rather than a second rule.
				MemberIds = IsMemberMode && SelectedPerson is not null
					? [SelectedPerson.Id]
					: [],
				Committee = IsCommitteeMode ? Committee?.Slug ?? string.Empty : string.Empty,
				ReplyToId = ReplyToId,
			}).ConfigureAwait(true);

			if (!result.Succeeded)
			{
				Error = result.Error;
				return;
			}

			await Shell.Current.GoToAsync("..").ConfigureAwait(true);
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>
	/// A message needs something to say and somebody to say it to.
	///
	/// <para>The audience check is not just tidiness: Fellowship refuses a
	/// send from a handset with no audience, on the grounds that
	/// addressing the whole fellowship is a broadcast and belongs to
	/// whoever holds the capability in WordPress. Better to grey the
	/// button than to explain a 400.</para>
	/// </summary>
	private bool CanSend() =>
		!Busy
		&& !string.IsNullOrWhiteSpace(Subject)
		&& !string.IsNullOrWhiteSpace(Body)
		&& (IsMemberMode ? SelectedPerson is not null : Committee is not null);
}
