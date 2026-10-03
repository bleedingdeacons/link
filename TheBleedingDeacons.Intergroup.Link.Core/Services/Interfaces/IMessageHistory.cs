using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The clearable message history this handset keeps.
///
/// <para><b>Why there is one at all.</b> Fellowship deletes messages once
/// the retention window passes, and a member is entitled to keep their
/// own copy for longer — or to keep none. The server holds what it needs
/// for audit; the handset holds what its owner wants to read on a train.
/// </para>
///
/// <para><b>Why it is clearable, and what clearing actually does.</b>
/// <see cref="ClearAsync"/> deletes the local store and remembers how far
/// it had got, so what was cleared stays cleared. It does not tell the
/// server anything and it does not un-send anything: other people still
/// have their copies, and the intergroup's own record is untouched. That
/// is worth saying plainly on the screen that offers it, because "clear
/// history" reads to most people like "delete the messages", and it is
/// only ever this phone's copies.</para>
///
/// <para><b>Why remembering is the whole trick.</b> A poll asks for
/// everything above <see cref="PollFromAsync"/>, so a store that merely
/// deleted its file went back to asking from zero and the server refilled
/// it within seconds — leaving a button whose entire visible effect was
/// nothing at all. Clearing therefore leaves a mark behind, and the next
/// poll starts no lower than that mark.</para>
///
/// <para><b>Which is exactly why signing out must not use it.</b> The
/// mark belongs to the member who set it. Left in place for whoever signs
/// in next, it would hand them an inbox that silently refuses to fetch
/// their own history, with nothing on screen to explain why — so sign-out
/// calls <see cref="ResetAsync"/> instead.</para>
/// </summary>
public interface IMessageHistory
{
	/// <summary>Everything held, newest first.</summary>
	Task<IReadOnlyList<LinkMessage>> AllAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Add or replace messages, keyed by id.
	///
	/// <para>Replace rather than skip: the same message arrives by push
	/// and again by poll, and the poll's copy carries the read flag. A
	/// store that ignored the second copy would show a message as unread
	/// forever after it was read on another device.</para>
	/// </summary>
	Task SaveAsync(IEnumerable<LinkMessage> messages, CancellationToken cancellationToken = default);

	/// <summary>
	/// Where the next poll should start: the highest id a poll has
	/// collected, the point a clear reached, or 0 when neither applies.
	///
	/// <para><b>Not the highest id held.</b> A push puts a message in the
	/// history without saying anything about the ones before it. Ids are
	/// shared across the whole fellowship, so a gap below a pushed message
	/// cannot be told apart from messages that were somebody else's. Until
	/// 2026-10-03 the poll started from the highest id held: message 9's
	/// push dropped, message 10's arrived, and the next sync asked for
	/// everything above 10, so 9 never came. Only
	/// <see cref="MarkPolledAsync"/> moves this, and only a sync calls
	/// that.</para>
	///
	/// <para>The higher of the two, not the newer. Taking whichever was set
	/// last would, after a clear, ask for everything just cleared.</para>
	/// </summary>
	Task<long> PollFromAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Record that a poll has collected everything it is going to up to
	/// <paramref name="messageId"/>. Never moves the mark backwards.
	/// </summary>
	Task MarkPolledAsync(long messageId, CancellationToken cancellationToken = default);

	Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Record that Fellowship has accepted this phone's acknowledgement of
	/// these messages. See <see cref="LinkMessage.Acknowledged"/>.
	/// </summary>
	Task MarkAcknowledgedAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default);

	/// <summary>
	/// What this member has sent from this phone, newest first.
	///
	/// <para>In the same store as the inbox, so it is cleared, reset and
	/// adopted with it — a phone handed on must not carry away what its
	/// last member wrote any more than what they were sent.</para>
	/// </summary>
	Task<IReadOnlyList<SentMessage>> SentAsync(CancellationToken cancellationToken = default);

	/// <summary>Keep a copy of a message this phone has just sent.</summary>
	Task SaveSentAsync(SentMessage message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Apply the server's latest counts to the sent messages they are for.
	/// Answers the ids of those that changed, or nothing.
	/// </summary>
	Task<IReadOnlyList<long>> ApplyReceiptsAsync(IEnumerable<MessageReceipt> receipts, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete this phone's copy of one message, received or sent.
	///
	/// <para>Like <see cref="ClearAsync"/>, and for the same reasons: it
	/// does not unsend anything and it does not tell the server. What
	/// stays behind is a <see cref="DeletedMessage"/> — two ids — so the
	/// conversation it sat in is not cut in two and a second copy
	/// arriving later is not taken back in.</para>
	/// </summary>
	Task DeleteAsync(long messageId, CancellationToken cancellationToken = default);

	/// <summary>
	/// What is left of the messages deleted one at a time, for
	/// <see cref="Conversations.Build"/> to walk a conversation through.
	/// </summary>
	Task<IReadOnlyList<DeletedMessage>> DeletedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete this phone's copies, and remember how far they reached so
	/// the next poll does not fetch them straight back. The member's
	/// choice; see the interface remarks.
	/// </summary>
	Task ClearAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete everything, the mark left by <see cref="ClearAsync"/>
	/// included, leaving the store as it was before anybody signed in.
	///
	/// <para>For signing out, and for nothing else. The next member on
	/// this handset must start from zero and fetch their own history.</para>
	/// </summary>
	Task ResetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Say who this store now belongs to, emptying it first if it belonged
	/// to somebody else.
	///
	/// <para><b>Called at enrolment, and it is what makes losing
	/// authorisation safe.</b> A handset that is refused keeps its
	/// messages — see <c>AuthenticationLost</c> — because the usual cause
	/// is an administrator's change rather than a phone in the wrong
	/// hands, and a member should not lose their correspondence over a
	/// corrected email address. That decision is only defensible if the
	/// messages cannot then be read by whoever signs in next, and this is
	/// where that is enforced.</para>
	///
	/// <para>A store with no owner recorded is treated as somebody else's
	/// and emptied. That is every handset upgrading from a build before
	/// this existed, exactly once, and the alternative is a guess about
	/// whose messages they are.</para>
	/// </summary>
	/// <param name="memberId">The Unity member id now signed in.</param>
	Task AdoptAsync(long memberId, CancellationToken cancellationToken = default);
}
