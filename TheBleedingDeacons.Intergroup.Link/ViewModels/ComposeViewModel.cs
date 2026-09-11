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
/// <para><b>One list, any number of recipients.</b> Members and
/// committees sit together and are chosen by tapping; each becomes a chip
/// that can be removed. This replaced a pair of radios that made the
/// sender pick a scope before the screen would show them anything —
/// necessary while Fellowship refused a message addressed to a committee
/// and to named people at once, and pointless the moment it stopped
/// (2026-09-11). The server resolves the union and de-duplicates, so
/// somebody named who also sits on a chosen committee gets one copy.</para>
///
/// <para>Committees appear only when the site allows committee sends from
/// the app, which is off by default — Fellowship simply sends an empty
/// committee list, so there is nothing here to hide and no control to
/// grey out.</para>
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
	private readonly List<Recipient> _all = [];

	/// <summary>
	/// What is actually on screen: <see cref="_all"/> narrowed by
	/// <see cref="Search"/>, minus whatever is already chosen.
	/// </summary>
	public ObservableCollection<Recipient> Candidates { get; } = [];

	/// <summary>
	/// Who this message is for. Rendered as chips above the list, each
	/// removable.
	/// </summary>
	/// <remarks>
	/// A collection rather than two nullable properties, which is what it
	/// replaced. Fellowship refused a message addressed to members and a
	/// committee together until 2026-09-11, so the screen had a radio and
	/// exactly one of either could be chosen; with that rule gone, the
	/// question "which list is showing?" is one the sender should never
	/// have to answer.
	/// </remarks>
	public ObservableCollection<Recipient> Chosen { get; } = [];

	/// <summary>
	/// Narrows the list as it is typed into. Never sent anywhere.
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

	/// <summary>Whether anything has been chosen yet, for the empty state.</summary>
	public bool HasChosen => Chosen.Count > 0;

	/// <summary>
	/// The inverse, as a property rather than a converter.
	///
	/// <para>This project has no inverse-bool converter and does not need
	/// one for a single screen; a second property is cheaper than a
	/// resource that every page then has to know about.</para>
	/// </summary>
	public bool HasChosenNothing => Chosen.Count == 0;

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

		_all.Clear();

		// Committees first. There are a handful of them against a few
		// hundred members, so alphabetical order across the lot would bury
		// them — and a committee is the choice somebody scrolling is most
		// likely to be looking for deliberately.
		//
		// A site that does not allow committee sends from the app is sent
		// none, so there is nothing here to hide: the list is simply
		// members.
		foreach (var committee in directory.Committees)
		{
			_all.Add(Recipient.ForCommittee(committee));
		}

		foreach (var member in directory.Members)
		{
			_all.Add(Recipient.ForMember(member));
		}

		ApplySearch();
	}

	/// <summary>
	/// Rebuild the visible list from the held one.
	///
	/// <para>Rebuilt rather than filtered in place, and it drops whatever
	/// is already chosen: a name in the list and the same name in a chip
	/// above it invites a second tap that does nothing.</para>
	/// </summary>
	private void ApplySearch()
	{
		var term = Search.Trim();

		Candidates.Clear();
		foreach (var candidate in _all)
		{
			if (candidate.Matches(term) && !IsChosen(candidate))
			{
				Candidates.Add(candidate);
			}
		}
	}

	private bool IsChosen(Recipient recipient) =>
		Chosen.Any(c => string.Equals(c.Key, recipient.Key, StringComparison.Ordinal));

	partial void OnSearchChanged(string value) => ApplySearch();

	/// <summary>
	/// Add a recipient, from a tap on the list.
	/// </summary>
	/// <remarks>
	/// The search box is cleared as well. Somebody who typed "sec" to find
	/// the Secretary is, the moment they have them, looking at a list
	/// narrowed by a word that has nothing to do with whoever they want
	/// next.
	/// </remarks>
	[RelayCommand]
	private void Choose(Recipient? recipient)
	{
		if (recipient is null || IsChosen(recipient))
		{
			return;
		}

		Chosen.Add(recipient);
		Search = string.Empty;

		// OnSearchChanged only fires when the value actually changes, and
		// it usually has not — the list still has to lose the row that
		// just became a chip.
		ApplySearch();

		Changed();
	}

	/// <summary>Remove a recipient, from the × on its chip.</summary>
	[RelayCommand]
	private void Drop(Recipient? recipient)
	{
		if (recipient is null)
		{
			return;
		}

		var held = Chosen.FirstOrDefault(c => string.Equals(c.Key, recipient.Key, StringComparison.Ordinal));
		if (held is null)
		{
			return;
		}

		Chosen.Remove(held);
		ApplySearch();

		Changed();
	}

	private void Changed()
	{
		OnPropertyChanged(nameof(HasChosen));
		OnPropertyChanged(nameof(HasChosenNothing));
		SendCommand.NotifyCanExecuteChanged();
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
				// Both lists, from one set of chips. Fellowship refused a
				// request carrying both until 2026-09-11; it now resolves
				// the union and de-duplicates, so somebody who is named and
				// also sits on a chosen committee gets one copy.
				MemberIds = [.. Chosen.Where(r => !r.IsCommittee).Select(r => r.MemberId)],
				Committees = [.. Chosen.Where(r => r.IsCommittee).Select(r => r.CommitteeSlug)],
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
		&& Chosen.Count > 0;
}
