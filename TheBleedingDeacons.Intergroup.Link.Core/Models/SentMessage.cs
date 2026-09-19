using System.Globalization;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// One message this member sent from this phone, and how far it has got.
///
/// <para><b>Kept here at the moment of sending, not fetched.</b> Fellowship
/// has no route that hands a sender their own messages back, and one would
/// have to seal them like the inbox does. This phone already had the
/// subject, the body and the names in its hands when it sent them, so it
/// keeps them — in the same encrypted history file as the inbox, cleared
/// and reset with it. What the server is asked for afterwards is only
/// counts; see <see cref="MessageReceipt"/>.</para>
///
/// <para>The cost is stated plainly: a message sent from another handset
/// does not appear here, and neither does one sent before this existed.
/// </para>
/// </summary>
public sealed record SentMessage
{
	public required long Id { get; init; }

	public string Subject { get; init; } = string.Empty;

	public string Body { get; init; } = string.Empty;

	/// <summary>
	/// Who it went to, as the sender chose them: "Jo B", or
	/// "Dave B, Literature". Names from the address book, never addresses.
	/// </summary>
	public string To { get; init; } = string.Empty;

	/// <summary>Unix seconds, as the server recorded the send.</summary>
	public long CreatedAt { get; init; }

	/// <summary>
	/// The message this one answered, or 0.
	///
	/// <para>Kept so a reply can be filed under the conversation it
	/// belongs to — see <see cref="Conversations"/>. Sent messages kept
	/// before this existed read as 0, and so each stands as a conversation
	/// of its own: the pointer went to the server and was never written
	/// down here, and nothing can recover it.</para>
	/// </summary>
	public long ReplyToId { get; init; }

	/// <summary>How many members it reached — a committee counts each of them.</summary>
	public int Recipients { get; init; }

	/// <summary>How many of them have it on a phone. A read counts.</summary>
	public int Received { get; init; }

	/// <summary>How many of them have read it.</summary>
	public int Read { get; init; }

	public DateTimeOffset Sent => DateTimeOffset.FromUnixTimeSeconds(CreatedAt);

	/// <inheritdoc cref="LinkMessage.SentLocal" />
	public DateTimeOffset SentLocal => Sent.ToLocalTime();

	/// <summary>
	/// The ticks.
	///
	/// <para><b>All of them, not any of them.</b> A tick that meant
	/// "somebody has it" would tell a sender their message reached the
	/// committee when it had reached one member of it. The count on the
	/// opened message is where "some of them" is said.</para>
	/// </summary>
	public ReceiptState State =>
		Recipients <= 0 ? ReceiptState.Sent
		: Read >= Recipients ? ReceiptState.Read
		: Received >= Recipients ? ReceiptState.Received
		: ReceiptState.Sent;

	/// <summary>
	/// Whether there is anything left to find out. A message everyone has
	/// read will not change again, so a sync stops asking about it.
	/// </summary>
	public bool Settled => State == ReceiptState.Read;

	public bool IsReceived => State is ReceiptState.Received or ReceiptState.Read;

	public bool IsRead => State == ReceiptState.Read;

	/// <summary>
	/// What the opened message says under the ticks.
	///
	/// <para>For one recipient the ticks already say everything, so this
	/// says it in words. For several it says the part the ticks cannot:
	/// how many.</para>
	/// </summary>
	public string Summary
	{
		get
		{
			if (Recipients <= 1)
			{
				return State switch
				{
					ReceiptState.Read => "Read",
					ReceiptState.Received => "Received",
					_ => "Sent",
				};
			}

			if (Read > 0)
			{
				return string.Create(CultureInfo.CurrentCulture, $"Read by {Read} of {Recipients}");
			}

			return Received > 0
				? string.Create(CultureInfo.CurrentCulture, $"Received by {Received} of {Recipients}")
				: string.Create(CultureInfo.CurrentCulture, $"Sent to {Recipients}");
		}
	}

	/// <summary>
	/// The same message with the server's latest counts, or this one
	/// unchanged when they say nothing new.
	///
	/// <para>Never walks a count backwards. The server's counts only rise,
	/// and a lower one means a recipient row was removed — a member erased,
	/// or the retention sweep — which is not a sender's message becoming
	/// less delivered.</para>
	/// </summary>
	public SentMessage With(MessageReceipt receipt)
	{
		ArgumentNullException.ThrowIfNull(receipt);

		return this with
		{
			Recipients = Math.Max(Recipients, receipt.Recipients),
			Received = Math.Max(Received, receipt.Received),
			Read = Math.Max(Read, receipt.Read),
		};
	}
}

/// <summary>What the ticks on a sent message show.</summary>
public enum ReceiptState
{
	/// <summary>One tick: the intergroup has it, and not every recipient's phone does yet.</summary>
	Sent,

	/// <summary>Two grey ticks: on every recipient's phone.</summary>
	Received,

	/// <summary>Two blue ticks: every recipient has read it.</summary>
	Read,
}

/// <summary>
/// How far one sent message has got, as Fellowship counts it: recipients,
/// how many of them have received it, how many have read it.
/// </summary>
public sealed record MessageReceipt(long Id, int Recipients, int Received, int Read);

/// <summary>
/// Sent when a sync learned something new about a sent message's receipts,
/// so the message list can redraw without being asked.
/// </summary>
/// <param name="MessageIds">The sent messages whose counts moved.</param>
public sealed record ReceiptsChanged(IReadOnlyList<long> MessageIds);
