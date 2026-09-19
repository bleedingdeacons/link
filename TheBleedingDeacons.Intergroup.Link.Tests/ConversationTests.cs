using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The edges of <see cref="Conversations.Build"/> that Conversations.feature
/// does not reach: pointers no real server would send, the same id in
/// both stores, and what the list row says.
/// </summary>
public sealed class ConversationTests
{
	[Fact]
	public void TwoMessagesAnsweringEachOtherDoNotLoop()
	{
		// Only a downward pointer is followed, so 12 → 13 is refused and
		// 13 → 12 is kept: one conversation, not a walk in a circle.
		var conversations = Conversations.Build(
			[Received(12, replyTo: 13), Received(13, replyTo: 12)],
			[]);

		var only = Assert.Single(conversations);
		Assert.Equal(12, only.Root.Id);
		Assert.Equal([13L], only.Replies.Select(r => r.Id));
	}

	[Fact]
	public void AMessageAnsweringItselfStandsAlone()
	{
		var only = Assert.Single(Conversations.Build([Received(12, replyTo: 12)], []));

		Assert.Equal(12, only.Root.Id);
		Assert.Empty(only.Replies);
	}

	[Fact]
	public void TheSentCopyWinsWhenAMemberReceivesTheirOwnMessage()
	{
		var only = Assert.Single(Conversations.Build(
			[Received(20)],
			[new SentMessage { Id = 20, To = "Literature", Recipients = 4 }]));

		Assert.True(only.Root.IsSent);
		Assert.Equal("To Literature", only.Root.Counterpart);
	}

	[Fact]
	public void RepliesAreOrderedByTimeBeforeId()
	{
		var only = Assert.Single(Conversations.Build(
			[Received(12, createdAt: 100), Received(14, replyTo: 12, createdAt: 200), Received(15, replyTo: 12, createdAt: 150)],
			[]));

		Assert.Equal([15L, 14L], only.Replies.Select(r => r.Id));
		Assert.Equal(14, only.Latest.Id);
	}

	[Fact]
	public void AnUnreadReplyMarksTheWholeConversation()
	{
		var only = Assert.Single(Conversations.Build(
			[Received(12, readAt: 1), Received(13, replyTo: 12)],
			[]));

		Assert.True(only.HasUnread);
	}

	[Fact]
	public void SentMessagesAreNeverUnread()
	{
		var only = Assert.Single(Conversations.Build([], [new SentMessage { Id = 20 }]));

		Assert.False(only.HasUnread);
	}

	[Theory]
	[InlineData(0, "Dave B")]
	[InlineData(1, "Dave B · 1 reply")]
	[InlineData(3, "Dave B · 3 replies")]
	public void TheRowSaysWhoAndHowManyReplies(int replies, string expected)
	{
		var messages = new List<LinkMessage> { Received(12) };
		messages.AddRange(Enumerable.Range(13, replies).Select(id => Received(id, replyTo: 12)));

		Assert.Equal(expected, Assert.Single(Conversations.Build(messages, [])).Summary);
	}

	[Fact]
	public void NothingAnswersMessageZero()
	{
		Assert.Null(Conversations.RepliedTo(0, [new SentMessage { Id = 20 }]));
	}

	[Fact]
	public void AForwardOfASentMessageSaysWhoItWentTo()
	{
		var draft = Forwarding.Prefill(ConversationEntry.From(
			new SentMessage { Id = 20, Subject = "Rota", Body = "Tuesdays.", To = "Jo B" }));

		Assert.Equal("Fwd: Rota", draft.Subject);
		Assert.Contains("To: Jo B", draft.Body, StringComparison.Ordinal);
		Assert.EndsWith("Tuesdays.", draft.Body, StringComparison.Ordinal);
	}

	[Fact]
	public void AForwardOfNoSubjectIsStillAForward()
	{
		Assert.Equal("Fwd:", Forwarding.Prefill(ConversationEntry.From(Received(12, subject: " "))).Subject);
	}

	private static LinkMessage Received(long id, long replyTo = 0, long createdAt = 1788000000, long? readAt = null, string subject = "Subject") =>
		new()
		{
			Id = id,
			ReplyToId = replyTo,
			CreatedAt = createdAt,
			ReadAt = readAt,
			Sender = "Dave B",
			Subject = subject,
		};
}
