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
	/// Sealed to a keypair this handset has never held — what every
	/// message looks like after a keystore entry is replaced.
	/// </summary>
	[Given(@"^message (\d+) was sealed to another handset$")]
	public void ForSomebodyElse(long id) => world.Waiting(world.EnvelopeForSomebodyElse(id));

	[Given(@"^messages ([\d and]+) were sealed to another handset$")]
	public void SeveralForSomebodyElse(string ids) =>
		world.Waiting([.. MessageSteps.Ids(ids).Select(world.EnvelopeForSomebodyElse)]);

	/// <summary>
	/// One byte of the ciphertext turned over. GCM authenticates, so this
	/// fails to open rather than decrypting to something plausible.
	/// </summary>
	[Given(@"^message (\d+) was altered in transit$")]
	public void Tampered(long id) => world.Waiting(Sealing.Tamper(world.Envelope(id)));

	[Then(@"^Fellowship was told once that this handset cannot read its messages$")]
	public void ReportedOnce() => world.Fellowship.KeyFaultReports.ShouldBe(1);

	[Then(@"^Fellowship was never told that this handset cannot read its messages$")]
	public void NeverReported() => world.Fellowship.KeyFaultReports.ShouldBe(0);

	[Then(@"^the sync reports a key fault$")]
	public void KeyFault() => world.LastSync.ShouldNotBeNull().KeyFault.ShouldBeTrue();

	[Then(@"^the sync reports no key fault$")]
	public void NoKeyFault() => world.LastSync.ShouldNotBeNull().KeyFault.ShouldBeFalse();
}
