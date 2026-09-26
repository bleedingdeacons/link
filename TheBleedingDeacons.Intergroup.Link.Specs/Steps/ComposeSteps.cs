using System.Globalization;
using System.Text.Json;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Picking who a message goes to, and what leaves the phone as a result.
/// </summary>
[Binding]
public sealed class ComposeSteps(World world)
{
	private long _memberId;

	/// <summary>
	/// An address book written as a feature file writes one: names joined
	/// by "and", with "the X committee" standing for a committee.
	/// </summary>
	[Given(@"^the address book holds (.+)$")]
	public void AddressBook(string names)
	{
		foreach (var name in names.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (name.StartsWith("the ", StringComparison.Ordinal) && name.EndsWith(" committee", StringComparison.Ordinal))
			{
				var committee = name["the ".Length..^" committee".Length];

				world.Addressable.Add(Recipient.ForCommittee(
					new DirectoryCommittee { Slug = committee.ToLowerInvariant(), Name = committee }));

				continue;
			}

			world.Addressable.Add(Recipient.ForMember(new DirectoryMember { Id = ++_memberId, Name = name }));
		}
	}

	[Given(@"^a member (.+) of (.+), who is the (.+)$")]
	public void AMemberWithStanding(string name, string group, string position) =>
		world.Addressable.Add(Recipient.ForMember(new DirectoryMember
		{
			Id = ++_memberId,
			Name = name,
			HomeGroup = group,
			Position = position,
		}));

	[Given(@"^a member whose group is ""(.*)"" and who is (a GSR|not a GSR) and whose position is ""(.*)""$")]
	public void AMemberRow(string group, string gsr, string position) =>
		world.Member = new DirectoryMember
		{
			Id = ++_memberId,
			Name = "Dave B",
			HomeGroup = group,
			IsGsr = string.Equals(gsr, "a GSR", StringComparison.Ordinal),
			Position = position,
		};

	[Given(@"^Fellowship will refuse the send, saying ""(.+)""$")]
	public void SendRefused(string reason) => world.Fellowship.Send = SendResult.Failed(reason);

	/// <summary>
	/// Chosen from the list, never typed — and de-duplicated by the key
	/// the recipient carries, because a member id and a committee id are
	/// different numbers from different tables and nothing stops them
	/// colliding.
	/// </summary>
	[When(@"^a message is addressed to (.+)$")]
	public void AddressedTo(string name)
	{
		var recipient = world.Addressable.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal))
			.ShouldNotBeNull($"'{name}' is not in this scenario's address book.");

		if (!world.Chosen.Exists(chosen => string.Equals(chosen.Key, recipient.Key, StringComparison.Ordinal)))
		{
			world.Chosen.Add(recipient);
		}
	}

	[When(@"^the list is searched for ""(.*)""$")]
	public void Search(string term)
	{
		world.Found.Clear();
		world.Found.AddRange(world.Addressable.Where(r => r.Matches(term)));
	}

	[When(@"^it is sent$")]
	[When(@"^a message is sent$")]
	public async Task Send() => world.LastSend = await world.Handset.SendAsync(Request(replyTo: 0));

	[When(@"^it is sent in reply to message (\d+)$")]
	public async Task SendReply(long replyTo) => world.LastSend = await world.Handset.SendAsync(Request(replyTo));

	[Then(@"^the send named (\d+) members? by id$")]
	public void NamedMembers(int count)
	{
		var sent = world.Fellowship.Sent.ShouldHaveSingleItem();

		sent.MemberIds.Count.ShouldBe(count);
		sent.MemberIds.ShouldAllBe(id => id > 0);
	}

	[Then(@"^the send named the committee ""(.+)""$")]
	public void NamedCommittee(string slug) =>
		world.Fellowship.Sent.ShouldHaveSingleItem().Committees.ShouldContain(slug);

	/// <summary>
	/// The claim the whole directory design exists to keep. Asserted over
	/// the request as it would go on the wire rather than field by field,
	/// so a field added later is covered without anybody remembering to
	/// come back here — which is the failure mode worth guarding against,
	/// since a name or an address would be added by somebody who thought
	/// it was harmless.
	/// </summary>
	[Then(@"^the send carried no address of any kind$")]
	public void NoAddresses()
	{
		var wire = JsonSerializer.Serialize(world.Fellowship.Sent.ShouldHaveSingleItem());

		wire.ShouldNotContain("@");

		// Not even the names, which the app has and the server does not
		// need: it resolves an id to a member itself.
		foreach (var member in world.Chosen.Where(r => !r.IsCommittee))
		{
			wire.ShouldNotContain(member.Name);
		}
	}

	[Then(@"^the send answered message (\d+)$")]
	public void Answered(long replyTo) =>
		world.Fellowship.Sent.ShouldHaveSingleItem().ReplyToId.ShouldBe(replyTo);

	[Then(@"^the copy kept on this phone answers message (\d+)$")]
	public async Task KeptAnswering(long replyTo) =>
		(await world.History.SentAsync()).ShouldHaveSingleItem().ReplyToId.ShouldBe(replyTo);

	[Then(@"^the send answered nothing$")]
	public void AnsweredNothing() =>
		world.Fellowship.Sent.ShouldHaveSingleItem().ReplyToId.ShouldBe(0);

	[Then(@"^the send failed$")]
	public void Failed() => world.LastSend.ShouldNotBeNull().Succeeded.ShouldBeFalse();

	[Then(@"^the reason given is ""(.+)""$")]
	public void Reason(string reason) => world.LastSend.ShouldNotBeNull().Error.ShouldBe(reason);

	[Then(@"^nothing was sent to the server$")]
	public void NothingSent() => world.Fellowship.Sent.ShouldBeEmpty();

	[Then(@"^their second line reads ""(.*)""$")]
	public void SecondLine(string line) => world.Member.ShouldNotBeNull().Standing.ShouldBe(line);

	[Then(@"^(.+) (appears|is not here)$")]
	public void FoundOrNot(string name, string verdict) =>
		world.Found.Exists(r => string.Equals(r.Name, name, StringComparison.Ordinal))
			.ShouldBe(string.Equals(verdict, "appears", StringComparison.Ordinal));

	[Then(@"^the (.+) row's second line reads ""(.+)""$")]
	public void RowDetail(string name, string detail) =>
		world.Addressable.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal))
			.ShouldNotBeNull()
			.Detail.ShouldBe(detail);

	// ── Who a reply starts addressed to ───────────────────────────────

	private const string Stranger = "somebody not in the address book";

	private ConversationEntry? _original;

	private Recipient? _addressee;

	[Given(@"^(.+) has no registered device$")]
	public void NoDevice(string name)
	{
		var index = world.Addressable.IndexOf(Named(name));
		world.Addressable[index] = world.Addressable[index] with { HasDevice = false };
	}

	/// <summary>
	/// A message received from somebody, carrying their member id as
	/// Fellowship's envelope does. The name travels too, and deliberately
	/// agrees with the id: what is being proved is that the id is what
	/// chooses, which only shows where two members share the name.
	/// </summary>
	[Given(@"^a message from (.+)$")]
	public void From(string name)
	{
		var sender = string.Equals(name, Stranger, StringComparison.Ordinal)
			? new Recipient { Key = "m:999", Name = "Pat Q", MemberId = 999 }
			: Named(name);

		_original = new ConversationEntry { Id = 12, Sender = sender.Name, SenderId = sender.MemberId };
	}

	[Given(@"^a message this member sent to (.+)$")]
	public void SentTo(string name) =>
		_original = new ConversationEntry { Id = 12, IsSent = true, To = Named(name).Name };

	[Given(@"^a message composed in WordPress admin$")]
	public void FromAdmin() => _original = new ConversationEntry { Id = 12, Sender = "Intergroup Secretary" };

	[When(@"^it is replied to$")]
	public void RepliedTo() =>
		_addressee = Replying.Addressee(world.Addressable, Replying.AddressFor(_original.ShouldNotBeNull()));

	[Then(@"^the reply starts addressed to nobody$")]
	public void AddressedToNobody() => _addressee.ShouldBeNull();

	[Then(@"^the reply starts addressed to (?!nobody$)(.+)$")]
	public void AddressedToSomebody(string name) =>
		_addressee.ShouldNotBeNull().Key.ShouldBe(Named(name).Key);

	/// <summary>
	/// "Jo B", or "the second Dave B" where the address book holds two.
	/// </summary>
	private Recipient Named(string name)
	{
		var nth = 0;

		foreach (var (word, index) in new[] { ("the first ", 0), ("the second ", 1), ("the third ", 2) })
		{
			if (name.StartsWith(word, StringComparison.Ordinal))
			{
				name = name[word.Length..];
				nth = index;
				break;
			}
		}

		var matches = world.Addressable.Where(r => string.Equals(r.Name, name, StringComparison.Ordinal)).ToList();
		matches.Count.ShouldBeGreaterThan(nth, $"'{name}' is not in this scenario's address book that many times.");

		return matches[nth];
	}

	private SendRequest Request(long replyTo) => new()
	{
		Subject = "September intergroup",
		Body = "Moved to the 14th, same room.",
		MemberIds = [.. world.Chosen.Where(r => !r.IsCommittee).Select(r => r.MemberId)],
		Committees = [.. world.Chosen.Where(r => r.IsCommittee).Select(r => r.CommitteeSlug)],
		ReplyToId = replyTo,
	};
}
