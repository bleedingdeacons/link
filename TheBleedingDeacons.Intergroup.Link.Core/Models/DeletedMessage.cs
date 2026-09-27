namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What is kept of a message the member deleted from this phone: its id
/// and the id it answered, and nothing else.
///
/// <para><b>Why anything is kept at all.</b> Two reasons, and neither
/// needs a word of the message. A conversation is walked through
/// <c>reply_to</c>, so deleting the middle of an exchange would otherwise
/// cut it in two: everything after the gap would stand alone as though
/// its original had never been here. And the same message can arrive
/// again — a committee this member sits on receives what they sent it —
/// so the store has to know not to take it back.</para>
///
/// <para>No subject, no body, no sender: nothing a member who deleted
/// something would be surprised to find is still on the phone.</para>
/// </summary>
public sealed record DeletedMessage(long Id, long ReplyToId);
