using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Who a reply starts addressed to. Composing.feature says why; this holds
/// the edges.
/// </summary>
public sealed class ReplyingTests
{
	private static readonly Recipient[] Directory =
	[
		Recipient.ForCommittee(new DirectoryCommittee { Slug = "42", Name = "Steering" }),
		Recipient.ForMember(new DirectoryMember { Id = 7, Name = "Dave B" }),
		Recipient.ForMember(new DirectoryMember { Id = 42, Name = "Dave B" }),
		Recipient.ForMember(new DirectoryMember { Id = 9, Name = "Jo B", HasDevice = false }),
	];

	[Fact]
	public void AReceivedMessageIsAnsweredToItsSender() =>
		Assert.Equal(42, Replying.AddressFor(new ConversationEntry { Id = 12, Sender = "Dave B", SenderId = 42 }));

	[Fact]
	public void ThisMembersOwnMessageIsAnsweredToNobody()
	{
		// A sent copy never carries a sender id, but the rule should not
		// depend on that staying true.
		Assert.Equal(0, Replying.AddressFor(new ConversationEntry { Id = 12, IsSent = true, SenderId = 42 }));
	}

	[Fact]
	public void ANegativeSenderIdIsNobody() =>
		Assert.Equal(0, Replying.AddressFor(new ConversationEntry { Id = 12, SenderId = -3 }));

	[Fact]
	public void TheIdChoosesBetweenMembersSharingAName()
	{
		var addressee = Replying.Addressee(Directory, 42);

		Assert.NotNull(addressee);
		Assert.Equal("m:42", addressee.Key);
	}

	[Fact]
	public void ACommitteeIsNeverChosenForAMemberId()
	{
		// Slug "42" and member 42 are different things, and the committee
		// comes first in the list.
		Assert.False(Replying.Addressee(Directory, 42)!.IsCommittee);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(9)]
	[InlineData(999)]
	public void NobodyIsChosenWithoutSomebodyToReach(long memberId) =>
		Assert.Null(Replying.Addressee(Directory, memberId));
}
