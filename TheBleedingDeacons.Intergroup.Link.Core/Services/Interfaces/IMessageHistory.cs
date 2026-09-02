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
/// <see cref="ClearAsync"/> deletes the local store outright. It does not
/// tell the server anything and it does not un-send anything: other
/// people still have their copies, and a message still on the server
/// arrives again on the next poll if it has not aged out. That is worth
/// saying plainly on the screen that offers it, because "clear history"
/// reads to most people like "delete the messages", and it is not.</para>
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

	/// <summary>The highest message id held, or 0. This is what a poll asks for.</summary>
	Task<long> HighestIdAsync(CancellationToken cancellationToken = default);

	Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default);

	/// <summary>Delete everything held locally. See the interface remarks.</summary>
	Task ClearAsync(CancellationToken cancellationToken = default);
}
