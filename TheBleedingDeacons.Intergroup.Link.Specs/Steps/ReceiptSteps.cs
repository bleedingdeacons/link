using System.Globalization;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Receipts, from both ends: what a recipient is shown about a message —
/// whether it is new, and when it was sent — and what its sender is shown
/// about how far it has got.
/// </summary>
[Binding]
public sealed class ReceiptSteps(World world)
{
	private const string Moment = @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}) UTC";

	// ── The recipient ─────────────────────────────────────────────────

	// The dot and the bold subject on MessagesPage are both DataTriggers
	// on IsRead, and nothing else. So this is the indicator, one binding
	// removed — which is as close as a test host gets to a XAML trigger.
	[Then(@"^message (\d+) is marked as new$")]
	public async Task MarkedNew(long id) =>
		(await world.HeldAsync(id)).ShouldNotBeNull().IsRead.ShouldBeFalse();

	[Then(@"^message (\d+) is not marked as new$")]
	public async Task NotMarkedNew(long id) =>
		(await world.HeldAsync(id)).ShouldNotBeNull().IsRead.ShouldBeTrue();

	[Given(@"^message (\d+) was sent at " + Moment + " and is waiting on the server$")]
	public void SentAndWaiting(long id, string moment) =>
		world.ServerHolds(id, createdAt: UnixSeconds(moment));

	[When(@"^message (\d+), sent at " + Moment + ", arrives by push$")]
	public async Task SentAndPushed(long id, string moment)
	{
		var envelope = world.Envelope(id, createdAt: UnixSeconds(moment));

		world.Opened = await world.Handset.ReceivePushAsync(envelope.WrappedKey, envelope.Payload);
	}

	[Then(@"^message (\d+) reads as sent at " + Moment + "$")]
	public async Task ReadsAsSent(long id, string moment) =>
		(await world.HeldAsync(id)).ShouldNotBeNull().Sent
			.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(UnixSeconds(moment)));

	[Then(@"^Fellowship was told message (\d+) was received$")]
	public void Acknowledged(long id) => world.Fellowship.Acknowledged.ShouldContain(id);

	[Then(@"^Fellowship was told message (\d+) was received (once|twice)$")]
	public void AcknowledgedTimes(long id, string times) =>
		world.Fellowship.Acknowledged.Count(acknowledged => acknowledged == id).ShouldBe(Times(times));

	[Then(@"^Fellowship was never told message (\d+) was received$")]
	public void NeverAcknowledged(long id) => world.Fellowship.Acknowledged.ShouldNotContain(id);

	// ── The sender ────────────────────────────────────────────────────

	/// <summary>
	/// Send through the handset, so the copy it keeps is the one the
	/// shipping code keeps — and put the recipients' rows on the server,
	/// all unopened, as the send would.
	/// </summary>
	[Given(@"^this member sent message (\d+) to (.+)$")]
	[When(@"^this member sends message (\d+) to (.+)$")]
	public async Task Sends(long id, string names)
	{
		var recipients = names.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		world.Fellowship.Send = new SendResult { MessageId = id, Recipients = recipients.Length, CreatedAt = 1788000000 };
		world.Fellowship.Recipients[id] = recipients.ToDictionary(name => name, _ => new RecipientRow(false, false));

		world.LastSend = await world.Handset.SendAsync(new SendRequest
		{
			Subject = "Literature order",
			Body = "Twelve Big Books for Tuesday.",
			MemberIds = [.. Enumerable.Range(1, recipients.Length).Select(n => (long)n)],
			To = string.Join(", ", recipients),
		});
	}

	[Given(@"^(\w+ \w) has read message (\d+), but the receipt for opening it never arrived$")]
	public void ReadWithoutReceipt(string name, long id) => Row(id, name, received: false, read: true);

	[Given(@"^(\w+ \w) (has not opened|has opened|has read) message (\d+)$")]
	public void HasDone(string name, string what, long id) =>
		Row(id, name, received: what != "has not opened", read: what == "has read");

	[Given(@"^Fellowship does not know about receipts$")]
	public void NoReceipts()
	{
		world.Fellowship.AcknowledgementAccepted = false;
		world.Fellowship.ReceiptsAvailable = false;
	}

	[When(@"^Fellowship learns about receipts$")]
	public void ReceiptsArrive()
	{
		world.Fellowship.AcknowledgementAccepted = true;
		world.Fellowship.ReceiptsAvailable = true;
	}

	[Then(@"^message (\d+) is kept on this phone, to ""(.+)""$")]
	public async Task KeptWithRecipients(long id, string to) =>
		(await SentAsync(id)).To.ShouldBe(to);

	/// <summary>
	/// "Shows as received" is true of a message that has also been read —
	/// two ticks are two ticks — so it is asked as IsReceived rather than
	/// as an exact state. "Sent" is exact: one tick, and no more.
	/// </summary>
	[Then(@"^message (\d+) shows as (sent|received|read)$")]
	public async Task ShowsAs(long id, string state)
	{
		var sent = await SentAsync(id);

		switch (state)
		{
			case "sent":
				sent.State.ShouldBe(ReceiptState.Sent);
				break;
			case "received":
				sent.IsReceived.ShouldBeTrue();
				break;
			default:
				sent.IsRead.ShouldBeTrue();
				break;
		}
	}

	[Then(@"^message (\d+) does not show as received$")]
	public async Task NotReceived(long id) => (await SentAsync(id)).IsReceived.ShouldBeFalse();

	[Then(@"^message (\d+) does not show as read$")]
	public async Task NotRead(long id) => (await SentAsync(id)).IsRead.ShouldBeFalse();

	[Then(@"^message (\d+) says ""(.+)""$")]
	public async Task Says(long id, string summary) => (await SentAsync(id)).Summary.ShouldBe(summary);

	[Then(@"^Fellowship was asked about message (\d+) (once|twice)$")]
	public void AskedAbout(long id, string times) =>
		world.Fellowship.AskedForReceipts.Count(asked => asked == id).ShouldBe(Times(times));

	private void Row(long id, string name, bool received, bool read)
	{
		var rows = world.Fellowship.Recipients[id];
		rows.ContainsKey(name).ShouldBeTrue($"{name} was not sent message {id}");

		rows[name] = new RecipientRow(received, read);
	}

	private async Task<SentMessage> SentAsync(long id) =>
		(await world.History.SentAsync()).FirstOrDefault(m => m.Id == id).ShouldNotBeNull();

	private static int Times(string times) => times == "once" ? 1 : 2;

	private static long UnixSeconds(string moment) =>
		DateTimeOffset.ParseExact(
				moment,
				"yyyy-MM-dd HH:mm",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal)
			.ToUnixTimeSeconds();
}
