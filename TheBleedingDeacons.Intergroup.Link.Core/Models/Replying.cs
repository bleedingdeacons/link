namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Who a reply starts out addressed to.
///
/// <para><b>Whoever sent the message being answered</b>, chosen for the
/// member rather than left for them to find. Compose used to open a reply
/// with nobody chosen, and Send stays grey until somebody is, so a reply
/// looked broken until the sender was searched for by hand. That's the
/// wrong way round for the commonest case.</para>
///
/// <para><b>By member id, never by name.</b> Fellowship puts the sender's
/// opaque Unity id in the envelope beside their name, and the directory
/// lists members under that same id. An intergroup has more than one
/// Dave B, so matching on the name would sometimes choose the wrong
/// person, and nobody would notice.</para>
///
/// <para>The sender is chosen, not locked in. They appear as an ordinary
/// chip, so the member can remove them or add other people.</para>
///
/// <para>Nobody is chosen when there is nothing safe to choose: a reply to
/// this member's own message (a sent copy keeps names, not ids), a message
/// composed in WordPress admin (no member behind it), one held from before
/// Fellowship sent the id, or a sender with no registered device.</para>
/// </summary>
public static class Replying
{
	/// <summary>
	/// The member id a reply to <paramref name="original"/> should be
	/// addressed to, or 0 for nobody.
	/// </summary>
	public static long AddressFor(ConversationEntry original)
	{
		ArgumentNullException.ThrowIfNull(original);

		return original.IsSent ? 0 : Math.Max(0, original.SenderId);
	}

	/// <summary>
	/// The recipient in <paramref name="directory"/> that
	/// <paramref name="memberId"/> names, or null when there is none that
	/// can be sent to.
	/// </summary>
	public static Recipient? Addressee(IEnumerable<Recipient> directory, long memberId)
	{
		ArgumentNullException.ThrowIfNull(directory);

		if (memberId <= 0)
		{
			return null;
		}

		return directory.FirstOrDefault(r => !r.IsCommittee && r.MemberId == memberId && r.HasDevice);
	}
}
