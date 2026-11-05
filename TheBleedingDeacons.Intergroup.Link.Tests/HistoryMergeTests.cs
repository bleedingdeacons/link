using System.Security.Cryptography;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Taking a backup's history in: what joins, what stays gone, and what the
/// marks do afterwards. The rule under all of it is that a restore cannot
/// bring back what the member removed, whichever came first.
/// </summary>
public sealed class HistoryMergeTests : IDisposable
{
	private const long Member = 7;

	private readonly string _directory = Path.Combine(Path.GetTempPath(), "link-tests-" + Guid.NewGuid().ToString("N"));
	private readonly JsonMessageHistory _history;

	public HistoryMergeTests()
	{
		_history = new JsonMessageHistory(Path.Combine(_directory, "messages.bin"), RandomNumberGenerator.GetBytes(32));
		_history.AdoptAsync(Member).GetAwaiter().GetResult();
	}

	public void Dispose()
	{
		_history.Dispose();

		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Fact]
	public async Task Messages_join_by_id_and_are_counted_once()
	{
		await _history.SaveAsync([Received(12), Received(13)]);

		var merge = await _history.MergeAsync(Member, new HistorySnapshot { Messages = [Received(13), Received(14)] });

		Assert.Equal(HistoryMergeOutcome.Merged, merge.Outcome);
		Assert.Equal(1, merge.Added);
		Assert.Equal(0, merge.StayedGone);
		Assert.Equal([14L, 13L, 12L], await ReceivedIds());
	}

	[Fact]
	public async Task A_message_read_on_either_side_is_read()
	{
		await _history.SaveAsync([Received(12), Received(13) with { ReadAt = 500 }]);

		await _history.MergeAsync(Member, new HistorySnapshot
		{
			Messages = [Received(12) with { ReadAt = 400 }, Received(13)],
		});

		var held = await _history.AllAsync();

		Assert.Equal(500, held.Single(m => m.Id == 13).ReadAt);
		Assert.Equal(400, held.Single(m => m.Id == 12).ReadAt);
	}

	[Fact]
	public async Task A_message_acknowledged_on_either_side_is_acknowledged()
	{
		await _history.SaveAsync([Received(12), Received(13)]);
		await _history.MarkAcknowledgedAsync([13]);

		await _history.MergeAsync(Member, new HistorySnapshot
		{
			Messages = [Received(12) with { Acknowledged = true }, Received(13)],
		});

		Assert.All(await _history.AllAsync(), m => Assert.True(m.Acknowledged));
	}

	[Fact]
	public async Task A_sent_copy_on_both_sides_keeps_the_higher_counts_and_its_pointer()
	{
		await _history.SaveSentAsync(Sent(15) with { Recipients = 5, Received = 3, Read = 1 });

		await _history.MergeAsync(Member, new HistorySnapshot
		{
			Sent = [Sent(15) with { Recipients = 5, Received = 2, Read = 2, ReplyToId = 9 }],
		});

		var held = Assert.Single(await _history.SentAsync());

		Assert.Equal(3, held.Received);
		Assert.Equal(2, held.Read);
		Assert.Equal(9, held.ReplyToId);
	}

	[Fact]
	public async Task A_message_deleted_since_the_backup_stays_deleted()
	{
		await _history.SaveAsync([Received(12), Received(13)]);
		await _history.SaveSentAsync(Sent(14));

		var backup = await Backup();

		await _history.DeleteAsync(13);
		await _history.DeleteAsync(14);

		var merge = await _history.MergeAsync(Member, backup);

		Assert.Equal(0, merge.Added);
		Assert.Equal(2, merge.StayedGone);
		Assert.Equal([12L], await ReceivedIds());
		Assert.Empty(await _history.SentAsync());
	}

	[Fact]
	public async Task A_message_deleted_in_the_backup_goes_from_this_phone()
	{
		await _history.SaveAsync([Received(12), Received(13) with { ReplyToId = 12 }]);
		await _history.SaveSentAsync(Sent(14));

		await _history.MergeAsync(Member, new HistorySnapshot { Deleted = [new DeletedMessage(13, 12), new DeletedMessage(14, 0)] });

		Assert.Equal([12L], await ReceivedIds());
		Assert.Empty(await _history.SentAsync());
		Assert.Contains(new DeletedMessage(13, 12), await _history.DeletedAsync());
	}

	[Fact]
	public async Task A_message_deleted_on_either_side_is_refused_when_it_arrives_again()
	{
		await _history.MergeAsync(Member, new HistorySnapshot { Deleted = [new DeletedMessage(13, 0)] });

		await _history.SaveAsync([Received(13)]);

		Assert.Empty(await _history.AllAsync());
	}

	[Fact]
	public async Task A_deleted_record_with_a_pointer_beats_one_without()
	{
		await _history.SaveSentAsync(Sent(13));
		await _history.DeleteAsync(13);

		await _history.MergeAsync(Member, new HistorySnapshot { Deleted = [new DeletedMessage(13, 12)] });

		Assert.Equal(new DeletedMessage(13, 12), Assert.Single(await _history.DeletedAsync()));
	}

	[Fact]
	public async Task A_message_cleared_since_the_backup_stays_cleared()
	{
		await _history.SaveAsync([Received(12), Received(13)]);

		var backup = await Backup();

		await _history.ClearAsync();

		var merge = await _history.MergeAsync(Member, backup);

		Assert.Equal(0, merge.Added);
		Assert.Equal(2, merge.StayedGone);
		Assert.Empty(await _history.AllAsync());
		Assert.Equal(13, await _history.PollFromAsync());
	}

	[Fact]
	public async Task A_sent_copy_cleared_since_the_backup_stays_cleared_even_above_the_received_mark()
	{
		// The poll's mark stops at the highest message received, and has to,
		// because a sent id can sit above messages no poll has collected. So
		// a sent copy above it needs a mark of its own, or a backup from
		// before the clear brings it straight back.
		await _history.SaveAsync([Received(10)]);
		await _history.SaveSentAsync(Sent(15));

		var backup = await Backup();

		await _history.ClearAsync();

		await _history.MergeAsync(Member, backup);

		Assert.Empty(await _history.SentAsync());
		Assert.Empty(await _history.AllAsync());
	}

	[Fact]
	public async Task Clearing_sent_copies_does_not_move_where_the_next_poll_starts()
	{
		await _history.SaveAsync([Received(10)]);
		await _history.SaveSentAsync(Sent(15));

		await _history.ClearAsync();

		Assert.Equal(10, await _history.PollFromAsync());
	}

	[Fact]
	public async Task What_was_cleared_stays_cleared_through_signing_out_and_back_in()
	{
		await _history.SaveSentAsync(Sent(15));

		var backup = await Backup();

		await _history.ClearAsync();
		await _history.ResetAsync();
		await _history.AdoptAsync(Member);

		await _history.MergeAsync(Member, backup);

		Assert.Empty(await _history.SentAsync());
	}

	[Fact]
	public async Task What_signing_out_took_comes_back_because_the_member_did_not_choose_that()
	{
		await _history.SaveAsync([Received(12)]);
		await _history.SaveSentAsync(Sent(15));

		var backup = await Backup();

		await _history.ResetAsync();
		await _history.AdoptAsync(Member);

		var merge = await _history.MergeAsync(Member, backup);

		Assert.Equal(2, merge.Added);
		Assert.Equal([12L], await ReceivedIds());
		Assert.Single(await _history.SentAsync());
	}

	[Fact]
	public async Task The_sent_mark_goes_with_the_store_when_somebody_else_signs_in()
	{
		await _history.SaveSentAsync(Sent(15));
		await _history.ClearAsync();

		await _history.AdoptAsync(9);

		await _history.MergeAsync(9, new HistorySnapshot { Sent = [Sent(15)] });

		Assert.Single(await _history.SentAsync());
	}

	[Fact]
	public async Task A_clear_in_the_backup_goes_from_this_phone_too()
	{
		await _history.SaveAsync([Received(12), Received(13), Received(20)]);
		await _history.SaveSentAsync(Sent(11));
		await _history.SaveSentAsync(Sent(21));

		var merge = await _history.MergeAsync(Member, new HistorySnapshot { ClearedUpTo = 13, SentClearedUpTo = 19 });

		Assert.Equal(3, merge.StayedGone);
		Assert.Equal([20L], await ReceivedIds());
		Assert.Equal(21, Assert.Single(await _history.SentAsync()).Id);
		Assert.Equal(13, await _history.PollFromAsync());
	}

	[Fact]
	public async Task A_message_above_the_clear_on_this_phone_is_taken_in()
	{
		await _history.SaveAsync([Received(12)]);
		await _history.ClearAsync();

		var merge = await _history.MergeAsync(Member, new HistorySnapshot { Messages = [Received(12), Received(14)] });

		Assert.Equal(1, merge.Added);
		Assert.Equal(1, merge.StayedGone);
		Assert.Equal([14L], await ReceivedIds());
	}

	[Fact]
	public async Task A_message_the_backup_holds_below_its_own_mark_survived_that_clear()
	{
		// It reached the backup's phone after its clear, by a late push. The
		// clear did not take it, so the backup's mark should not either.
		var merge = await _history.MergeAsync(Member, new HistorySnapshot { Messages = [Received(9)], ClearedUpTo = 13 });

		Assert.Equal(1, merge.Added);
		Assert.Equal([9L], await ReceivedIds());
	}

	[Fact]
	public async Task A_message_on_this_phone_below_its_own_mark_is_not_taken_by_it()
	{
		await _history.SaveAsync([Received(13)]);
		await _history.ClearAsync();
		await _history.SaveAsync([Received(9)]);

		await _history.MergeAsync(Member, new HistorySnapshot { Messages = [Received(20)] });

		Assert.Equal([20L, 9L], await ReceivedIds());
	}

	[Fact]
	public async Task One_id_held_as_sent_and_received_counts_once_and_goes_as_one()
	{
		// A message to a committee this member sits on.
		var merge = await _history.MergeAsync(Member, new HistorySnapshot { Sent = [Sent(15)], Messages = [Received(15)] });

		Assert.Equal(1, merge.Added);
		Assert.Equal(0, merge.StayedGone);

		await _history.DeleteAsync(15);

		merge = await _history.MergeAsync(Member, new HistorySnapshot { Sent = [Sent(15)], Messages = [Received(15)] });

		Assert.Equal(0, merge.Added);
		Assert.Equal(1, merge.StayedGone);
	}

	[Fact]
	public async Task One_id_losing_only_its_sent_copy_has_not_gone()
	{
		await _history.SaveAsync([Received(15)]);
		await _history.SaveSentAsync(Sent(15));

		var merge = await _history.MergeAsync(Member, new HistorySnapshot { SentClearedUpTo = 15 });

		Assert.Equal(0, merge.StayedGone);
		Assert.Equal([15L], await ReceivedIds());
		Assert.Empty(await _history.SentAsync());
	}

	[Fact]
	public async Task The_marks_take_the_higher_of_the_two_and_never_walk_backwards()
	{
		await _history.SaveAsync([Received(20)]);
		await _history.MarkPolledAsync(30);
		await _history.ClearAsync();

		await _history.MergeAsync(Member, new HistorySnapshot { ClearedUpTo = 5, PolledUpTo = 6 });

		Assert.Equal(30, await _history.PollFromAsync());

		await _history.MergeAsync(Member, new HistorySnapshot { PolledUpTo = 40 });

		Assert.Equal(40, await _history.PollFromAsync());
	}

	[Fact]
	public async Task A_mark_the_backup_should_not_have_is_treated_as_none()
	{
		await _history.MergeAsync(Member, new HistorySnapshot { ClearedUpTo = -5, SentClearedUpTo = -5, PolledUpTo = -5 });

		Assert.Equal(0, await _history.PollFromAsync());
	}

	[Fact]
	public async Task What_is_restored_survives_being_reopened()
	{
		var path = Path.Combine(_directory, "reopened.bin");
		var key = RandomNumberGenerator.GetBytes(32);

		using (var writing = new JsonMessageHistory(path, key))
		{
			await writing.MergeAsync(Member, new HistorySnapshot
			{
				Messages = [Received(12)],
				Sent = [Sent(15)],
				Deleted = [new DeletedMessage(11, 0)],
				ClearedUpTo = 5,
				SentClearedUpTo = 6,
				PolledUpTo = 13,
			});
		}

		using var reading = new JsonMessageHistory(path, key);
		var backup = await reading.SnapshotAsync(Member);

		Assert.NotNull(backup);
		Assert.Single(backup.Messages);
		Assert.Single(backup.Sent);
		Assert.Single(backup.Deleted);
		Assert.Equal(5, backup.ClearedUpTo);
		Assert.Equal(6, backup.SentClearedUpTo);
		Assert.Equal(13, backup.PolledUpTo);
	}

	[Fact]
	public async Task Another_members_store_is_left_untouched()
	{
		await _history.SaveAsync([Received(12)]);

		var merge = await _history.MergeAsync(9, new HistorySnapshot { Messages = [Received(20)], ClearedUpTo = 99 });

		Assert.Equal(HistoryMergeOutcome.OtherMember, merge.Outcome);
		Assert.Equal([12L], await ReceivedIds());
		Assert.Equal(0, await _history.PollFromAsync());
		Assert.Null(await _history.SnapshotAsync(9));
	}

	[Fact]
	public async Task A_store_with_no_owner_becomes_this_members()
	{
		var path = Path.Combine(_directory, "unowned.bin");
		var key = RandomNumberGenerator.GetBytes(32);
		using var history = new JsonMessageHistory(path, key);

		await history.SaveAsync([Received(12)]);

		var merge = await history.MergeAsync(Member, new HistorySnapshot { Messages = [Received(20)] });

		Assert.Equal(HistoryMergeOutcome.Merged, merge.Outcome);
		Assert.Equal(2, (await history.AllAsync()).Count);

		// Recorded now, so the same member signing back in keeps it all.
		await history.AdoptAsync(Member);

		Assert.Equal(2, (await history.AllAsync()).Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task Nobody_is_not_a_member(long memberId)
	{
		var merge = await _history.MergeAsync(memberId, new HistorySnapshot { Messages = [Received(20)] });

		Assert.Equal(HistoryMergeOutcome.OtherMember, merge.Outcome);
		Assert.Null(await _history.SnapshotAsync(memberId));
	}

	[Fact]
	public async Task Ids_that_cannot_be_messages_and_repeats_are_passed_over()
	{
		var merge = await _history.MergeAsync(Member, new HistorySnapshot
		{
			Messages = [Received(0), Received(-3), Received(12), Received(12), null!],
			Sent = [Sent(0), Sent(15), Sent(15), null!],
			Deleted = [new DeletedMessage(0, 0), null!],
		});

		Assert.Equal(2, merge.Added);
		Assert.Equal([12L], await ReceivedIds());
		Assert.Single(await _history.SentAsync());
		Assert.Empty(await _history.DeletedAsync());
	}

	private async Task<HistorySnapshot> Backup() => (await _history.SnapshotAsync(Member))!;

	private async Task<IEnumerable<long>> ReceivedIds() => (await _history.AllAsync()).Select(m => m.Id);

	private static LinkMessage Received(long id) => new() { Id = id, Subject = "Message " + id };

	private static SentMessage Sent(long id) => new() { Id = id, Subject = "Sent " + id, To = "Jo B" };
}
