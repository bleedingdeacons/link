namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// One message inside a conversation, whichever way it travelled.
///
/// <para>A conversation mixes what this member received with what they
/// sent, and the two are held apart for good reasons — a received message
/// has a read flag and an acknowledgement, a sent one has receipts — so
/// this is the shape both are read through rather than a replacement for
/// either. It is built fresh from the history every time and never
/// stored.</para>
/// </summary>
public sealed record ConversationEntry
{
	public required long Id { get; init; }

	/// <summary>The message this one answers, or 0.</summary>
	public long ReplyToId { get; init; }

	/// <summary>Unix seconds, as the server recorded it.</summary>
	public long CreatedAt { get; init; }

	public string Subject { get; init; } = string.Empty;

	public string Body { get; init; } = string.Empty;

	/// <summary>True when this member sent it from this phone.</summary>
	public bool IsSent { get; init; }

	/// <summary>Who it came from, on a received message.</summary>
	public string Sender { get; init; } = string.Empty;

	/// <summary>Who it came from as a member id, on a received message, or 0. See <see cref="Replying"/>.</summary>
	public long SenderId { get; init; }

	/// <summary>Who it went to, on a sent message.</summary>
	public string To { get; init; } = string.Empty;

	/// <summary>
	/// Whether it has been read here. A sent message is never unread: the
	/// member wrote it.
	/// </summary>
	public bool IsRead { get; init; } = true;

	/// <summary>The ticks, on a sent message.</summary>
	public ReceiptState Receipt { get; init; }

	/// <summary>The ticks in words, on a sent message.</summary>
	public string ReceiptSummary { get; init; } = string.Empty;

	public DateTimeOffset SentLocal => DateTimeOffset.FromUnixTimeSeconds(CreatedAt).ToLocalTime();

	public bool IsReceived => !IsSent;

	/// <summary>"Dave B", or "To Jo B": who is on the other end, either way round.</summary>
	public string Counterpart
	{
		get
		{
			if (!IsSent)
			{
				return Sender;
			}

			return To.Length > 0 ? "To " + To : string.Empty;
		}
	}

	public static ConversationEntry From(LinkMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);

		return new ConversationEntry
		{
			Id = message.Id,
			ReplyToId = message.ReplyToId,
			CreatedAt = message.CreatedAt,
			Subject = message.Subject,
			Body = message.Body,
			Sender = message.Sender,
			SenderId = message.SenderId,
			IsRead = message.IsRead,
		};
	}

	public static ConversationEntry From(SentMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);

		return new ConversationEntry
		{
			Id = message.Id,
			ReplyToId = message.ReplyToId,
			CreatedAt = message.CreatedAt,
			Subject = message.Subject,
			Body = message.Body,
			IsSent = true,
			To = message.To,
			Receipt = message.State,
			ReceiptSummary = message.Summary,
		};
	}
}
