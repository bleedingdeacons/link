using System.Globalization;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Conversations: which messages sit together, in what order, and what
/// a message says about having been answered.
///
/// <para>Asserted against <see cref="Conversations.Build"/> over the real
/// history, which is exactly what the message list draws from. The list
/// itself is MAUI and out of reach; everything it decides is here.</para>
/// </summary>
[Binding]
public sealed class ConversationSteps(World world)
{
	private ForwardDraft? _forward;

	[Given(@"^message (\d+), answering (\d+), arrives by push$")]
	[When(@"^message (\d+), answering (\d+), arrives by push$")]
	public async Task AnswerArrivesByPush(long id, long answering)
	{
		var envelope = world.Envelope(id, replyTo: answering);

		world.Opened = await world.Handset.ReceivePushAsync(envelope.WrappedKey, envelope.Payload);
	}

	[Given(@"^message (\d+), answering (\d+), is waiting on the server$")]
	public void AnswerWaiting(long id, long answering) => world.ServerHolds(id, replyTo: answering);

	[Given(@"^message (\d+), with the subject ""(.+)"", is held$")]
	public async Task HeldWithSubject(long id, string subject)
	{
		world.ServerHolds(id, subject: subject, sender: "Dave B");

		await world.Handset.SyncAsync();
	}

	/// <summary>
	/// Sent through the handset, as Compose sends a reply, so the copy it
	/// keeps is the one the shipping code keeps — pointer and all.
	/// </summary>
	[When(@"^this member replies to message (\d+) with message (\d+)$")]
	public async Task Replies(long answering, long id) =>
		world.LastSend = await SendAsync(id, answering, "Re: Literature order", "Yes, Tuesday is fine.", "Jo B");

	[When(@"^message (\d+) is prepared for forwarding$")]
	public async Task PrepareForward(long id) => _forward = Forwarding.Prefill(await EntryAsync(id));

	/// <summary>
	/// What Compose does with a forward: prefill from the original, pick
	/// a recipient, send with no reply pointer.
	/// </summary>
	[When(@"^message (\d+) is forwarded to (.+) as message (\d+)$")]
	public async Task Forward(long original, string name, long id)
	{
		_forward = Forwarding.Prefill(await EntryAsync(original));

		var recipient = world.Addressable.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal))
			.ShouldNotBeNull($"'{name}' is not in this scenario's address book.");

		world.Fellowship.Send = new SendResult { MessageId = id, Recipients = 1, CreatedAt = 1788000000 };
		world.Fellowship.Recipients[id] = new Dictionary<string, RecipientRow>(StringComparer.Ordinal)
		{
			[name] = new RecipientRow(false, false),
		};

		world.LastSend = await world.Handset.SendAsync(new SendRequest
		{
			Subject = _forward.Subject,
			Body = _forward.Body,
			MemberIds = [recipient.MemberId],
			To = name,
		});
	}

	[Then(@"^the conversations read ([\d, ]+)$")]
	public async Task ConversationsRead(string ids) =>
		(await ConversationsAsync()).Select(c => c.Root.Id).ShouldBe(Ids(ids));

	[Then(@"^conversation (\d+) holds replies ([\d, ]+)$")]
	public async Task HoldsReplies(long root, string ids) =>
		(await ConversationAsync(root)).Replies.Select(r => r.Id).ShouldBe(Ids(ids));

	[Then(@"^conversation (\d+) has no replies$")]
	public async Task NoReplies(long root) =>
		(await ConversationAsync(root)).Replies.ShouldBeEmpty();

	[Then(@"^the forward's subject reads ""(.+)""$")]
	public void ForwardSubject(string subject) => _forward.ShouldNotBeNull().Subject.ShouldBe(subject);

	[Then(@"^the forward's body quotes message (\d+)$")]
	public async Task ForwardQuotes(long id)
	{
		var original = await EntryAsync(id);
		var body = _forward.ShouldNotBeNull().Body;

		body.ShouldContain(Forwarding.Divider);
		body.ShouldContain("From: " + original.Sender);
		body.ShouldContain("Subject: " + original.Subject);
		body.ShouldEndWith(original.Body);
	}

	[Then(@"^message (\d+) shows it was answered with message (\d+)$")]
	public async Task AnsweredWith(long id, long answer) =>
		Conversations.RepliedTo(id, await world.History.SentAsync()).ShouldNotBeNull().Id.ShouldBe(answer);

	[Then(@"^message (\d+) shows no answer$")]
	public async Task NoAnswer(long id) =>
		Conversations.RepliedTo(id, await world.History.SentAsync()).ShouldBeNull();

	private static List<long> Ids(string list) =>
		[.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(id => long.Parse(id, CultureInfo.InvariantCulture))];

	private async Task<IReadOnlyList<Conversation>> ConversationsAsync() =>
		Conversations.Build(await world.History.AllAsync(), await world.History.SentAsync());

	private async Task<Conversation> ConversationAsync(long root) =>
		(await ConversationsAsync()).FirstOrDefault(c => c.Root.Id == root)
			.ShouldNotBeNull($"Message {root} does not start a conversation.");

	private async Task<ConversationEntry> EntryAsync(long id) =>
		(await ConversationsAsync()).SelectMany(c => c.Messages).FirstOrDefault(m => m.Id == id)
			.ShouldNotBeNull($"Message {id} is not on this phone.");

	private async Task<SendResult> SendAsync(long id, long answering, string subject, string body, string to)
	{
		world.Fellowship.Send = new SendResult { MessageId = id, Recipients = 1, CreatedAt = 1788000000 };
		world.Fellowship.Recipients[id] = new Dictionary<string, RecipientRow>(StringComparer.Ordinal)
		{
			[to] = new RecipientRow(false, false),
		};

		return await world.Handset.SendAsync(new SendRequest
		{
			Subject = subject,
			Body = body,
			MemberIds = [1],
			ReplyToId = answering,
			To = to,
		});
	}
}
