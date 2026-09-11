namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// One thing a message can be addressed to: a member, or a committee.
///
/// <para><b>Why the two share a type.</b> Compose used to hold them
/// apart — a radio switched between a member list and a committee list,
/// and exactly one of either could be chosen, because Fellowship refused
/// a message addressed to both. It no longer does, and once a message can
/// carry four names and two committees at once, "which list is showing?"
/// stops being a question worth asking the sender. One list, one search,
/// one row of chips.</para>
///
/// <para><b>Still no addresses.</b> A member is an opaque Unity id and a
/// committee is a slug; what goes back to the server is ids and slugs,
/// and the server does the addressing. Nothing here holds an email
/// address or a telephone number, which is the property the whole
/// directory design exists to keep.</para>
/// </summary>
public sealed record Recipient
{
	/// <summary>
	/// Identity for de-duplication and for removing a chip.
	///
	/// <para>Prefixed by kind, because a member id and a committee id are
	/// different numbers from different tables and nothing stops them
	/// colliding.</para>
	/// </summary>
	public required string Key { get; init; }

	/// <summary>What the chip and the row say.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// The second line in the list: a member's home group, GSR standing
	/// and service position, or the word that marks a committee out.
	/// Empty leaves the line out rather than holding a blank one open.
	/// </summary>
	public string Detail { get; init; } = string.Empty;

	/// <summary>The Unity member id, or 0 for a committee.</summary>
	public long MemberId { get; init; }

	/// <summary>The committee slug, or empty for a member.</summary>
	public string CommitteeSlug { get; init; } = string.Empty;

	public bool IsCommittee => CommitteeSlug.Length > 0;

	public bool HasDetail => Detail.Length > 0;

	/// <summary>
	/// Whether <paramref name="term"/> matches. Name, and whatever the
	/// second line says: somebody who wants the Secretary, or the GSR from
	/// Tuesday Bristol, is describing a person the only way they can.
	/// </summary>
	public bool Matches(string term) =>
		term.Length == 0
		|| Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
		|| Detail.Contains(term, StringComparison.CurrentCultureIgnoreCase);

	public static Recipient ForMember(DirectoryMember member)
	{
		ArgumentNullException.ThrowIfNull(member);

		return new Recipient
		{
			Key = "m:" + member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			Name = member.Name,
			Detail = member.Standing,
			MemberId = member.Id,
		};
	}

	public static Recipient ForCommittee(DirectoryCommittee committee)
	{
		ArgumentNullException.ThrowIfNull(committee);

		return new Recipient
		{
			Key = "c:" + committee.Slug,
			Name = committee.Name.Length > 0 ? committee.Name : committee.Slug,
			// Said on the row rather than left to the reader: "Literature"
			// is a plausible name for a person, and sending the fellowship's
			// business to a committee by mistake cannot be taken back.
			Detail = "Committee",
			CommitteeSlug = committee.Slug,
		};
	}
}
