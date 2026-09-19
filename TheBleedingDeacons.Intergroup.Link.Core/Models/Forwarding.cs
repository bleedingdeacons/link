using System.Globalization;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What Compose starts with when a message is passed on.
///
/// <para><b>A forward answers nothing.</b> It is sent with no
/// <c>reply_to</c>, so it starts a conversation of its own rather than
/// joining the one it came from. The people it goes to were not part of
/// that exchange, and folding their answers back into it would put words
/// in front of the original correspondents that were never addressed to
/// them.</para>
///
/// <para><b>The original travels in the body, in the clear, as text.</b>
/// Fellowship has no forward route and does not need one: a forward is an
/// ordinary new message whose body happens to quote another. That is also
/// why the recipients start empty — who it goes to is the whole decision
/// being made.</para>
/// </summary>
public static class Forwarding
{
	public const string Divider = "---------- Forwarded message ----------";

	/// <summary>The subject and body a forward of <paramref name="original"/> starts from.</summary>
	public static ForwardDraft Prefill(ConversationEntry original)
	{
		ArgumentNullException.ThrowIfNull(original);

		var subject = original.Subject.Trim();

		// Prefilled once, as a reply is: forwarding a forward does not
		// grow a stack of prefixes.
		if (!subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
		{
			subject = subject.Length > 0 ? "Fwd: " + subject : "Fwd:";
		}

		var who = original.IsSent
			? "To: " + original.To
			: "From: " + original.Sender;

		var when = original.SentLocal.ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);

		var body = string.Join(
			"\n",
			string.Empty,
			string.Empty,
			Divider,
			who,
			"Sent: " + when,
			"Subject: " + original.Subject,
			string.Empty,
			original.Body);

		return new ForwardDraft(subject, body);
	}
}
