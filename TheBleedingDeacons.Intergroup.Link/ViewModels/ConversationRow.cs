using CommunityToolkit.Mvvm.ComponentModel;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.ViewModels;

/// <summary>
/// One row on the message list: a conversation, and whether it is ticked
/// while the list is choosing what to delete.
///
/// <para>A wrapper because <see cref="Models.Conversation"/> is a record
/// rebuilt from the history on every load, and a tick has to be something
/// the checkbox can watch change. Held on the row rather than in a set the
/// page keeps, because the list recycles its cells: a tick that lived on a
/// cell would turn up on whichever conversation scrolled into it next.</para>
/// </summary>
public sealed partial class ConversationRow : ObservableObject
{
	public ConversationRow(Conversation conversation)
	{
		ArgumentNullException.ThrowIfNull(conversation);

		Conversation = conversation;
	}

	public Conversation Conversation { get; }

	[ObservableProperty]
	private bool _isChecked;
}
