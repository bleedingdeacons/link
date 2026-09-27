namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Who the compose screen lists: every member in the address book, or only
/// those with Link registered on a handset.
///
/// <para><b>It only hides; it never lets through.</b> A member with no
/// registered device cannot be chosen either way, because a message to them
/// individually would reach no phone. All this decides is whether they are
/// shown greyed out or not shown at all. See
/// <see cref="Models.Recipient.IsListed"/> for the rule.</para>
///
/// <para>The member's own choice, on this handset, rather than the
/// intergroup's. Fellowship still sends the whole address book with
/// <c>hasDevice</c> on each entry, so turning this off shows the rest
/// straight away, with no request to anybody.</para>
/// </summary>
public interface IRecipientListing
{
	/// <summary>
	/// Whether to leave out members with no registered device. Persisted,
	/// and read by the compose screen each time it rebuilds its list.
	/// </summary>
	bool OnlyRegistered { get; set; }
}
