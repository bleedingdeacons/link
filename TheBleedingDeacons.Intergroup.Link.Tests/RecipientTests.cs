using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The one type the compose screen addresses with, now that members and
/// committees share a list.
/// </summary>
public sealed class RecipientTests
{
	[Fact]
	public void AMemberAndACommitteeCannotCollideOnTheirKey()
	{
		// The keys are what de-duplicates chips and what removes one. A
		// member id and a committee slug come from different tables and
		// nothing stops the numbers matching, so the kind is part of the
		// key rather than assumed from context.
		var member = Recipient.ForMember(new DirectoryMember { Id = 7, Name = "Dave P" });
		var committee = Recipient.ForCommittee(new DirectoryCommittee { Slug = "7", Name = "Steering" });

		Assert.NotEqual(member.Key, committee.Key);
	}

	[Fact]
	public void AMemberCarriesItsIdAndNoSlug()
	{
		var member = Recipient.ForMember(new DirectoryMember
		{
			Id = 23742,
			Name = "Dave P",
			HomeGroup = "11th Step Monday",
			IsGsr = true,
			Position = "Intergroup Secretary",
		});

		Assert.False(member.IsCommittee);
		Assert.Equal(23742, member.MemberId);
		Assert.Equal(string.Empty, member.CommitteeSlug);

		// The second line is the standing the directory composed, not a
		// second opinion about it.
		Assert.Equal("11th Step Monday · GSR · Intergroup Secretary", member.Detail);
	}

	[Fact]
	public void ACommitteeCarriesItsSlugAndNoMemberId()
	{
		var committee = Recipient.ForCommittee(new DirectoryCommittee { Slug = "steering", Name = "Steering" });

		Assert.True(committee.IsCommittee);
		Assert.Equal("steering", committee.CommitteeSlug);
		Assert.Equal(0, committee.MemberId);
	}

	[Fact]
	public void ACommitteeSaysThatItIsOne()
	{
		// "Literature" is a plausible name for a person, and sending the
		// fellowship's business to a committee by mistake cannot be taken
		// back.
		var committee = Recipient.ForCommittee(new DirectoryCommittee { Slug = "literature", Name = "Literature" });

		Assert.Equal("Committee", committee.Detail);
	}

	[Fact]
	public void ACommitteeWithNoNameFallsBackToItsSlug()
	{
		var committee = Recipient.ForCommittee(new DirectoryCommittee { Slug = "steering" });

		Assert.Equal("steering", committee.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("dave")]
	[InlineData("DAVE")]
	[InlineData("11th step")]
	[InlineData("secretary")]
	public void SearchMatchesTheNameAndWhateverTheSecondLineSays(string term)
	{
		// Somebody who wants the Secretary, or the GSR from Tuesday
		// Bristol, is describing a person the only way they can.
		var member = Recipient.ForMember(new DirectoryMember
		{
			Id = 1,
			Name = "Dave P",
			HomeGroup = "11th Step Monday",
			IsGsr = true,
			Position = "Intergroup Secretary",
		});

		Assert.True(member.Matches(term));
	}

	[Fact]
	public void SearchDoesNotMatchSomebodyElse()
	{
		var member = Recipient.ForMember(new DirectoryMember { Id = 1, Name = "Dave P" });

		Assert.False(member.Matches("sue"));
	}

	[Fact]
	public void ACommitteeIsFoundByTypingItsName()
	{
		var committee = Recipient.ForCommittee(new DirectoryCommittee { Slug = "pi", Name = "Public Information" });

		Assert.True(committee.Matches("public"));
	}
}
