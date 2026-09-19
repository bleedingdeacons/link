namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Turn what this phone holds into conversations.
///
/// <para><b>Derived from <c>reply_to</c>, and from nothing else.</b> Every
/// message already carries the id it answers, received and — since
/// conversations — sent. A thread id invented now would have to be
/// guessed for every message already held, and the guess would be this
/// same walk.</para>
///
/// <para><b>What starts one.</b> A message that answers nothing; a
/// forward, which is sent answering nothing on purpose; and a reply whose
/// original is not on this phone — cleared, aged out, or sent from
/// another handset. That last one stands alone until the original turns
/// up, and joins it when it does, because nothing about it is stored.</para>
/// </summary>
public static class Conversations
{
	/// <summary>
	/// Every conversation, the one with the newest message first.
	///
	/// <para><b>A pointer is only followed downwards.</b> Fellowship's ids
	/// rise with time, so a reply always answers a lower id than its own.
	/// A pointer that claims otherwise is not a thread, and following it
	/// is how a pair of messages each answering the other would walk in a
	/// circle; refusing it makes the walk finite without a visited set.</para>
	///
	/// <para><b>Sent wins a tie.</b> A member who writes to a committee
	/// they sit on receives their own message, under the same id. The sent
	/// copy carries the ticks and knows who it went to, so it is the one
	/// kept.</para>
	/// </summary>
	public static IReadOnlyList<Conversation> Build(
		IEnumerable<LinkMessage> received,
		IEnumerable<SentMessage> sent)
	{
		ArgumentNullException.ThrowIfNull(received);
		ArgumentNullException.ThrowIfNull(sent);

		var byId = new Dictionary<long, ConversationEntry>();

		foreach (var message in received)
		{
			byId[message.Id] = ConversationEntry.From(message);
		}

		foreach (var message in sent)
		{
			byId[message.Id] = ConversationEntry.From(message);
		}

		return [.. byId.Values
			.GroupBy(entry => RootOf(entry, byId).Id)
			.Select(group =>
			{
				var ordered = group.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();
				var root = byId[group.Key];

				return new Conversation
				{
					Root = root,
					Replies = [.. ordered.Where(e => e.Id != root.Id)],
				};
			})
			.OrderByDescending(c => c.Latest.CreatedAt)
			.ThenByDescending(c => c.Latest.Id)];
	}

	/// <summary>
	/// The conversation holding this message, or null when it is not on
	/// this phone.
	/// </summary>
	public static Conversation? Containing(IReadOnlyList<Conversation> conversations, long messageId)
	{
		ArgumentNullException.ThrowIfNull(conversations);

		return conversations.FirstOrDefault(c => c.Messages.Any(m => m.Id == messageId));
	}

	/// <summary>
	/// The newest message this member sent from this phone in answer to
	/// this one, or null — what the message view says "You replied" from.
	/// </summary>
	public static SentMessage? RepliedTo(long messageId, IEnumerable<SentMessage> sent)
	{
		ArgumentNullException.ThrowIfNull(sent);

		return sent
			.Where(m => m.ReplyToId == messageId && messageId > 0)
			.OrderByDescending(m => m.CreatedAt)
			.ThenByDescending(m => m.Id)
			.FirstOrDefault();
	}

	private static ConversationEntry RootOf(ConversationEntry entry, Dictionary<long, ConversationEntry> byId)
	{
		var current = entry;

		while (current.ReplyToId > 0
			&& current.ReplyToId < current.Id
			&& byId.TryGetValue(current.ReplyToId, out var parent))
		{
			current = parent;
		}

		return current;
	}
}
