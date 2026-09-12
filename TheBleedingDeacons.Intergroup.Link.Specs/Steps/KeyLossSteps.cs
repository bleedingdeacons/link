using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Envelopes this handset cannot open, and what it does about them.
/// </summary>
[Binding]
public sealed class KeyLossSteps(World world)
{
	/// <summary>
	/// The server is still sealing to a key this handset no longer holds.
	///
	/// <para>Which is what a lost keystore entry actually looks like from
	/// the wire: Fellowship goes on sealing to the public half it was
	/// given, knowing nothing, and every message it sends arrives
	/// unopenable until the handset presents a replacement.</para>
	/// </summary>
	[Given(@"^message (\d+) is waiting, sealed to a key this handset has lost$")]
	public async Task SealedToALostKey(long id)
	{
		world.ServerHolds(id);

		// A new pair on the handset, and Fellowship not told. The server's
		// copy stays where it was, which is the whole asymmetry.
		await world.Keys.RegenerateAsync();
	}

	[Given(@"^messages ([\d and]+) are waiting, sealed to a key this handset has lost$")]
	public async Task SeveralSealedToALostKey(string ids)
	{
		foreach (var id in MessageSteps.Ids(ids))
		{
			world.ServerHolds(id);
		}

		await world.Keys.RegenerateAsync();
	}

	/// <summary>
	/// One byte of the ciphertext turned over. GCM authenticates, so this
	/// fails to open rather than decrypting to something plausible.
	/// </summary>
	[Given(@"^message (\d+) was altered in transit$")]
	public void Tampered(long id)
	{
		world.ServerHolds(id);
		world.Fellowship.Tampered.Add(id);
	}

	[When(@"^this handset presents its new key to Fellowship$")]
	public async Task PresentsKey() => await world.PresentKeyAsync();

	[Then(@"^Fellowship was told once that this handset cannot read its messages$")]
	public void ReportedOnce() => world.Fellowship.KeyFaultReports.ShouldBe(1);

	[Then(@"^Fellowship was never told that this handset cannot read its messages$")]
	public void NeverReported() => world.Fellowship.KeyFaultReports.ShouldBe(0);

	[Then(@"^the sync reports a key fault$")]
	public void KeyFault() => world.LastSync.ShouldNotBeNull().KeyFault.ShouldBeTrue();

	[Then(@"^the recovery is offered$")]
	public void RecoveryOffered() => world.KeyFaultAnnounced.ShouldBe(true);

	[Then(@"^the recovery is not offered$")]
	public void RecoveryNotOffered() => world.KeyFaultAnnounced.ShouldBe(false);

	[Then(@"^nothing has been said about the recovery$")]
	public void NothingSaid() => world.KeyFaultAnnounced.ShouldBeNull();

	[Then(@"^the sync reports no key fault$")]
	public void NoKeyFault() => world.LastSync.ShouldNotBeNull().KeyFault.ShouldBeFalse();
}
