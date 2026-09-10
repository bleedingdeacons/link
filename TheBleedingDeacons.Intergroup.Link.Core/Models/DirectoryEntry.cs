namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// One name in the address book Link shows when composing.
///
/// <para><b>There is no email address here, and that is the point.</b>
/// Fellowship hands out anonymous names and opaque member ids, and does
/// the addressing itself when the app sends the id back. So a stolen
/// handset yields a list of first names rather than the intergroup's
/// contact database, and a message cannot be addressed to somebody who
/// is not a member by inventing an address.</para>
/// </summary>
public sealed record DirectoryMember
{
	public required long Id { get; init; }

	public string Name { get; init; } = string.Empty;

	/// <summary>
	/// The member's home group, or empty.
	///
	/// <para>Here because a first name identifies nobody in an intergroup
	/// with several Daves — the same reason Hand shows one beside its own
	/// member list. Empty is ordinary rather than exceptional: a member
	/// need not have a group recorded, a group can be deleted while
	/// members still point at it, and a Fellowship older than this sends
	/// no group at all.</para>
	///
	/// <para><b>Not a contact detail.</b> Fellowship sends no email
	/// address and no telephone number, and adding this did not change
	/// that — its own test asserts as much.</para>
	/// </summary>
	public string HomeGroup { get; init; } = string.Empty;

	/// <summary>
	/// Whether this member is a General Service Representative.
	///
	/// <para>A bool rather than a label, so the wording lives in the app
	/// and never has to come back from the server.</para>
	/// </summary>
	public bool IsGsr { get; init; }

	/// <summary>
	/// The second line of a row, or empty when there is nothing to put in
	/// it.
	///
	/// <para>Composed here rather than in the XAML so the empty case has
	/// one answer: a member with no group who is not a GSR gets no line
	/// at all, rather than a blank one holding the row open.</para>
	/// </summary>
	public string Standing => (HomeGroup, IsGsr) switch
	{
		("", false) => string.Empty,
		("", true) => "GSR",
		(var group, false) => group,
		(var group, true) => group + " · GSR",
	};

	/// <summary>Whether <see cref="Standing"/> has anything to show.</summary>
	public bool HasStanding => Standing.Length > 0;
}

/// <summary>
/// One committee, addressed by slug.
///
/// <para>Present only when the site allows committee sends from the app,
/// which is off by default: sending to a committee by mistake cannot be
/// taken back. Showing a list the app is not allowed to use would be an
/// invitation to a refusal.</para>
/// </summary>
public sealed record DirectoryCommittee
{
	public required string Slug { get; init; }

	public string Name { get; init; } = string.Empty;

	/// <summary>
	/// The parent committee's Unity id, or 0 for a root committee.
	///
	/// <para>Carried so the picker can indent sub-committees. Sending to
	/// a parent reaches its descendants — Fellowship resolves the tree —
	/// so the indentation is telling the sender something real about how
	/// far the message will go.</para>
	/// </summary>
	public long ParentId { get; init; }
}

/// <summary>
/// The whole address book, as one response.
/// </summary>
public sealed record FellowshipDirectory
{
	public IReadOnlyList<DirectoryMember> Members { get; init; } = [];

	public IReadOnlyList<DirectoryCommittee> Committees { get; init; } = [];

	public static FellowshipDirectory Empty { get; } = new();
}
