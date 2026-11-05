namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Everything a <see cref="Services.Interfaces.IMessageHistory"/> holds,
/// taken out of the store to go into a backup or come back from one.
///
/// <para>The owner is not in here. The store answers only for the member it
/// is asked about, and a backup carries its member beside this rather than
/// inside it. See <see cref="BackupContents"/>.</para>
/// </summary>
public sealed record HistorySnapshot
{
	public IReadOnlyList<LinkMessage> Messages { get; init; } = [];

	public IReadOnlyList<SentMessage> Sent { get; init; } = [];

	public IReadOnlyList<DeletedMessage> Deleted { get; init; } = [];

	/// <summary>How far a clear reached among received messages.</summary>
	public long ClearedUpTo { get; init; }

	/// <summary>How far a clear reached among sent copies.</summary>
	public long SentClearedUpTo { get; init; }

	/// <summary>The highest id a poll has collected.</summary>
	public long PolledUpTo { get; init; }
}

/// <summary>What a merge did, or why it did nothing.</summary>
public enum HistoryMergeOutcome
{
	/// <summary>The store now holds both sides.</summary>
	Merged,

	/// <summary>The store belongs to another member, and is unchanged.</summary>
	OtherMember,

	/// <summary>The merged store could not be written, so the old one stands.</summary>
	NotWritten,
}

/// <summary>What a merge did to the store.</summary>
/// <param name="Outcome">Whether it happened at all.</param>
/// <param name="Added">Messages the store did not hold before.</param>
/// <param name="StayedGone">Messages left out because they were deleted or cleared on one side.</param>
public sealed record HistoryMerge(HistoryMergeOutcome Outcome, int Added = 0, int StayedGone = 0);
