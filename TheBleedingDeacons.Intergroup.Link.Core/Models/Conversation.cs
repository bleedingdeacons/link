using System.Globalization;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// A message that answers nothing, and everything since that answers it.
///
/// <para><b>One level deep.</b> A reply to a reply sits beside its
/// parent under the root, not beneath it, oldest first — a phone screen
/// read by a thumb has no room for an indented tree, and a conversation
/// between two people is a sequence far more often than it is a
/// branch.</para>
///
/// <para>Derived, never stored. See <see cref="Conversations.Build"/>.</para>
/// </summary>
public sealed record Conversation
{
	public required ConversationEntry Root { get; init; }

	/// <summary>Everything after the root, oldest first.</summary>
	public IReadOnlyList<ConversationEntry> Replies { get; init; } = [];

	/// <summary>The root and then the replies, in the order they were sent.</summary>
	public IReadOnlyList<ConversationEntry> Messages => [Root, .. Replies];

	/// <summary>The newest message in it, which is what orders the list.</summary>
	public ConversationEntry Latest => Replies.Count > 0 ? Replies[^1] : Root;

	public DateTimeOffset LatestLocal => Latest.SentLocal;

	/// <summary>Whether anything in it has not been read here — the strip on the list.</summary>
	public bool HasUnread => Messages.Any(m => !m.IsRead);

	public int ReplyCount => Replies.Count;

	public bool HasReplies => Replies.Count > 0;

	/// <summary>
	/// The list row's second line: who, and how many replies. "Dave B",
	/// "To Jo B · 1 reply", "Dave B · 3 replies".
	/// </summary>
	public string Summary => Replies.Count switch
	{
		0 => Root.Counterpart,
		1 => Join(Root.Counterpart, "1 reply"),
		_ => Join(Root.Counterpart, string.Create(CultureInfo.CurrentCulture, $"{Replies.Count} replies")),
	};

	private static string Join(string who, string replies) =>
		who.Length > 0 ? who + " · " + replies : replies;
}
