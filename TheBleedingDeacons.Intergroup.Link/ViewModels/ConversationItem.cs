using System.Globalization;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// One message on the conversation screen, with what the screen says
/// about it that the message itself does not know.
/// </summary>
public sealed class ConversationItem
{
	public ConversationItem(ConversationEntry entry, bool isRoot, SentMessage? answer)
	{
		ArgumentNullException.ThrowIfNull(entry);

		Entry = entry;
		IsRoot = isRoot;

		// Local time and spelled out, as the message view always has been:
		// the message somebody opened is the one they may want to quote a
		// date from, year and all.
		Sent = entry.SentLocal.ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);

		Subject = string.IsNullOrWhiteSpace(entry.Subject) ? "(no subject)" : entry.Subject;

		Replied = answer is null
			? string.Empty
			: "You replied · " + answer.SentLocal.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);
	}

	public ConversationEntry Entry { get; }

	/// <summary>The first message, whose subject heads the screen.</summary>
	public bool IsRoot { get; }

	/// <summary>The later ones carry their subject smaller, and only when it says something new.</summary>
	public bool IsReply => !IsRoot;

	public string Subject { get; }

	public string Sent { get; }

	/// <summary>"You replied · 3 Sep, 09:40", or empty.</summary>
	public string Replied { get; }

	public bool HasReplied => Replied.Length > 0;
}
