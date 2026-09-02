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
/// <para>The committee picker appears only when the site allows committee
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

	public ObservableCollection<DirectoryMember> People { get; } = [];

	public ObservableCollection<DirectoryCommittee> Committees { get; } = [];

	/// <summary>
	/// Who this message is for. Bound to a multi-select list.
	/// </summary>
	public ObservableCollection<object> SelectedPeople { get; } = [];

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

		People.Clear();
		foreach (var person in directory.Members)
		{
			People.Add(person);
		}

		Committees.Clear();
		foreach (var committee in directory.Committees)
		{
			Committees.Add(committee);
		}

		OnPropertyChanged(nameof(CanSendToCommittee));
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
				// whoever sent it. The picker enforces the same thing, so
				// this is belt and braces rather than a second rule.
				MemberIds = Committee is null ? SelectedIds() : [],
				Committee = Committee?.Slug ?? string.Empty,
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

	private IReadOnlyList<long> SelectedIds() =>
		SelectedPeople.OfType<DirectoryMember>().Select(p => p.Id).ToList();

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
		&& (Committee is not null || SelectedPeople.Count > 0);
}
