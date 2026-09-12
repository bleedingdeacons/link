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
/// everything above <see cref="HighestIdAsync"/>, so a store that merely
/// deleted its file went back to asking from zero and the server refilled
/// it within seconds — leaving a button whose entire visible effect was
/// nothing at all. Clearing therefore leaves a mark behind, and that mark
/// is where the next poll starts.</para>
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
	/// Where the next poll should start: the highest message id held, the
	/// point a clear reached, or 0 when neither applies.
	///
	/// <para>The higher of the two, not the newer. Clearing an inbox and
	/// then receiving one more message must not walk the starting point
	/// backwards to what was cleared.</para>
	/// </summary>
	Task<long> HighestIdAsync(CancellationToken cancellationToken = default);

	Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default);

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
