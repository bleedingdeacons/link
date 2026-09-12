using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// The state a scenario puts the phone in before anything arrives, and
/// the questions it asks about the phone itself rather than about one
/// message.
/// </summary>
[Binding]
public sealed class HandsetSteps(World world)
{
	[Given(@"^this handset is signed in to Fellowship$")]
	public void SignedIn() =>
		world.Sessions.Session = new() { Token = "device-token", MemberId = 7, MemberName = "Dave B" };

	[Given(@"^this handset is not signed in$")]
	public void NotSignedIn() => world.Sessions.Session = null;

	[Given(@"^Fellowship cannot be reached$")]
	public void Unreachable() => world.Fellowship.Inbox = InboxPage.Failed;

	/// <summary>
	/// A platform that has invalidated the keystore entry. Fellowship is
	/// not told and cannot tell — it goes on sealing to the public half it
	/// holds, which is exactly why <c>FakeDeviceKeyStore</c> keeps that
	/// half where it was.
	/// </summary>
	[Given(@"^this handset has lost its key$")]
	public async Task LostItsKey() => await world.Keys.ClearAsync();

	// Also a When: a scenario can replace the pair part-way through, after
	// a message has already been sealed to the old one, which is the case
	// that cannot be undone.
	[Given(@"^this handset generates a new keypair$")]
	[When(@"^this handset generates a new keypair$")]
	public async Task NewKeypair() => await world.Keys.RegenerateAsync();

	[When(@"^the app is killed and opened again$")]
	public void Restart() => world.Restart();

	[Then(@"^the server was never asked for anything$")]
	public void NeverAsked() => world.Fellowship.PolledSince.ShouldBeEmpty();

	/// <summary>
	/// The most recent poll, not any poll. A scenario that clears and then
	/// syncs would pass against an earlier one whatever the clear did.
	/// </summary>
	[Then(@"^the server was asked for everything above (\d+)$")]
	public void AskedAbove(long since)
	{
		world.Fellowship.PolledSince.ShouldNotBeEmpty();
		world.Fellowship.PolledSince[^1].ShouldBe(since);
	}
}
