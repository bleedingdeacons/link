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

	/// <summary>The "All GSRs" choice: neither a member nor a committee. See <see cref="ForAllGsrs"/>.</summary>
	public bool IsAllGsrs { get; init; }

	/// <summary>One person, addressed by <see cref="MemberId"/>.</summary>
	public bool IsMember => !IsCommittee && !IsAllGsrs;

	public bool HasDetail => Detail.Length > 0;

	/// <summary>
	/// Whether this recipient can be chosen. False only for a member with no
	/// registered device: a message to them individually would reach no
	/// phone. Always true for a committee.
	/// </summary>
	public bool HasDevice { get; init; } = true;

	/// <summary>The inverse of <see cref="HasDevice"/>, for the row's note.</summary>
	public bool HasNoDevice => !HasDevice;

	/// <summary>
	/// Whether this recipient appears in the compose list at all.
	///
	/// <para>With <paramref name="onlyRegistered"/> off, everybody is listed
	/// and a member with no device is shown but cannot be chosen, which
	/// tells the sender to reach them some other way. With it on, which is
	/// the default (see <c>IRecipientListing</c>), they are left out, so a
	/// fellowship where most members have never installed Link does not
	/// scroll past a page of names that cannot be tapped.</para>
	///
	/// <para>A committee is always listed. It reaches every member on it,
	/// registered or not, so the setting has nothing to say about it.</para>
	/// </summary>
	public bool IsListed(bool onlyRegistered) => !onlyRegistered || HasDevice;

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
			HasDevice = member.HasDevice,
		};
	}

	/// <summary>
	/// Everything the directory offers, in the order Compose lists it:
	/// All GSRs, then committees, then members.
	/// </summary>
	/// <remarks>
	/// <para><b>All GSRs first</b>: the widest of the choices, and the one
	/// most often wanted by name. Offered only when the server offers it —
	/// see <see cref="FellowshipDirectory.GsrCount"/>.</para>
	///
	/// <para><b>Then committees.</b> There are a handful of them against a
	/// few hundred members, so alphabetical order across the lot would
	/// bury them. A site that does not allow committee sends from the app
	/// is sent none, so there is nothing to hide here: the list is simply
	/// members.</para>
	/// </remarks>
	public static IReadOnlyList<Recipient> FromDirectory(FellowshipDirectory directory)
	{
		ArgumentNullException.ThrowIfNull(directory);

		var all = new List<Recipient>();

		if (directory.GsrCount > 0)
		{
			all.Add(ForAllGsrs(directory.GsrCount));
		}

		all.AddRange(directory.Committees.Select(ForCommittee));
		all.AddRange(directory.Members.Select(ForMember));

		return all;
	}

	/// <summary>
	/// Every GSR, as one choice at the top of the list.
	///
	/// <para><b>Resolved by Fellowship when it sends, not by ticking every
	/// GSR here.</b> The address book leaves out GSRs who have opted out of
	/// being listed, and caps how many members one send may name; a
	/// choice called "All GSRs" that quietly reached some of them would be
	/// worse than not offering it. So what travels is a flag, and the
	/// server works out who that is, as it does for a committee.</para>
	///
	/// <para>The second line says how many it reaches, because that number
	/// is the thing a sender cannot otherwise see — and a broadcast to
	/// forty people should look like one before it is sent.</para>
	/// </summary>
	/// <param name="count">How many GSRs, from <see cref="FellowshipDirectory.GsrCount"/>.</param>
	public static Recipient ForAllGsrs(int count) => new()
	{
		Key = "g:all",
		Name = "All GSRs",
		Detail = count == 1 ? "1 GSR" : count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " GSRs",
		IsAllGsrs = true,
	};

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
