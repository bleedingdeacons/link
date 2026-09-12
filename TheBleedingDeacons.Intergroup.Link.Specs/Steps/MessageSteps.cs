using System.Globalization;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Messages arriving, by either route, and what the phone has afterwards.
/// </summary>
[Binding]
public sealed class MessageSteps(World world)
{
	/// <summary>
	/// Ids as a feature file writes them: "12", or "12 and 13 and 14".
	/// </summary>
	public static IReadOnlyList<long> Ids(string list) =>
		list.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(id => long.Parse(id, CultureInfo.InvariantCulture))
			.ToList();

	// Also a Given: several scenarios put a message on the phone by
	// pushing one, because that is the shortest honest way to get it
	// there — the alternative writes to the history behind the code that
	// is supposed to be doing the writing.
	[Given(@"^message (\d+) arrives by push$")]
	[When(@"^message (\d+) arrives by push$")]
	public async Task ArrivesByPush(long id)
	{
		var envelope = world.Envelope(id);

		world.Opened = await world.Handset.ReceivePushAsync(envelope.WrappedKey, envelope.Payload);
	}

	/// <summary>
	/// The id beside an envelope is what lets a poll page without opening
	/// anything; the id the message is filed under is inside the seal.
	/// A push therefore ignores the outer one entirely, and this is the
	/// step that proves the two can disagree.
	/// </summary>
	[When(@"^an envelope labelled (\d+) carrying message (\d+) arrives by push$")]
	public async Task MislabelledPush(long label, long id)
	{
		var envelope = world.Envelope(id) with { Id = label };

		world.Opened = await world.Handset.ReceivePushAsync(envelope.WrappedKey, envelope.Payload);
	}

	[Given(@"^message (\d+) is waiting on the server$")]
	[When(@"^message (\d+) is waiting on the server$")]
	public void Waiting(long id) => world.ServerHolds(id);

	[Given(@"^messages ([\d and]+) are waiting on the server$")]
	[When(@"^messages ([\d and]+) are waiting on the server$")]
	public void SeveralWaiting(string ids)
	{
		foreach (var id in Ids(ids))
		{
			world.ServerHolds(id);
		}
	}

	[Given(@"^message (\d+) is waiting on the server, already read$")]
	[When(@"^message (\d+) is waiting on the server, already read$")]
	public void WaitingRead(long id) => world.ServerHolds(id, readAt: 1788000100);

	[Given(@"^message (\d+) is waiting on the server, still unread$")]
	[When(@"^message (\d+) is waiting on the server, still unread$")]
	public void WaitingUnread(long id) => world.ServerHolds(id);

	[Given(@"^the server says (\d+) are unread$")]
	public void UnreadTotal(int unread) => world.Fellowship.Unread = unread;

	/// <summary>
	/// Put a message on the phone without asking how it got there, for
	/// scenarios about what happens to one that is already held.
	/// </summary>
	[Given(@"^message (\d+) is held$")]
	public async Task AlreadyHeld(long id)
	{
		Waiting(id);

		await world.Handset.SyncAsync();
	}

	[Given(@"^messages ([\d and]+) are held$")]
	public async Task SeveralAlreadyHeld(string ids)
	{
		SeveralWaiting(ids);

		await world.Handset.SyncAsync();
	}

	[When(@"^the handset syncs$")]
	public async Task Sync() => world.LastSync = await world.Handset.SyncAsync();

	[Then(@"^the sync (succeeded|failed)$")]
	public void SyncOutcome(string outcome) =>
		world.LastSync.ShouldNotBeNull().Succeeded
			.ShouldBe(string.Equals(outcome, "succeeded", StringComparison.Ordinal));

	[Then(@"^(\d+) messages? (?:was|were) received$")]
	public void Received(int count) => world.LastSync.ShouldNotBeNull().Received.ShouldBe(count);

	[Then(@"^the unread count is (\d+)$")]
	public void Unread(int unread) => world.LastSync.ShouldNotBeNull().Unread.ShouldBe(unread);

	[Then(@"^the unread count is not known$")]
	public void UnreadUnknown() => world.LastSync.ShouldNotBeNull().Unread.ShouldBe(-1);

	[Then(@"^message (\d+) is held$")]
	public async Task IsHeld(long id) => (await world.HeldAsync(id)).ShouldNotBeNull();

	[Then(@"^nothing is held$")]
	public async Task NothingHeld() => (await world.HeldAsync()).ShouldBeEmpty();

	[Then(@"^(\d+) messages? (?:is|are) held$")]
	public async Task CountHeld(int count) => (await world.HeldAsync()).Count.ShouldBe(count);

	[Then(@"^the messages read ([\d, ]+)$")]
	public async Task InOrder(string ids)
	{
		var expected = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(id => long.Parse(id, CultureInfo.InvariantCulture));

		(await world.HeldAsync()).Select(m => m.Id).ShouldBe(expected);
	}

	[Then(@"^its subject reads ""(.+)""$")]
	public async Task Subject(string subject) =>
		(await world.HeldAsync()).ShouldHaveSingleItem().Subject.ShouldBe(subject);

	[Then(@"^nothing was opened$")]
	public void NothingOpened() => world.Opened.ShouldBeNull();

	[Then(@"^the list was told about message (\d+)$")]
	public void Announced(long id) => world.Announced.Select(m => m.Id).ShouldContain(id);

	[Then(@"^the list was told about nothing$")]
	public void AnnouncedNothing() => world.Announced.ShouldBeEmpty();
}
