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
