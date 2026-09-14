using System.Globalization;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// What a recipient is shown about a message: whether it is new, and when
/// it was sent.
///
/// <para>The sender's half of Receipts.feature has no bindings here, and
/// that is not an oversight: there is no sent view and no receipt on the
/// wire to bind it to. Those scenarios are tagged @ignore until there
/// is.</para>
/// </summary>
[Binding]
public sealed class ReceiptSteps(World world)
{
	private const string Moment = @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}) UTC";

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

	private static long UnixSeconds(string moment) =>
		DateTimeOffset.ParseExact(
				moment,
				"yyyy-MM-dd HH:mm",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal)
			.ToUnixTimeSeconds();
}
