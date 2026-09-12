using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>Reading a message, here and on the server.</summary>
[Binding]
public sealed class ReadSteps(World world)
{
	// Also a Given: a scenario can read a message and then ask what a
	// later sync does to it.
	[Given(@"^message (\d+) is read$")]
	[When(@"^message (\d+) is read$")]
	public async Task Read(long id) => await world.Handset.MarkReadAsync(id);

	[Then(@"^message (\d+) is read here$")]
	public async Task ReadHere(long id) =>
		(await world.HeldAsync(id)).ShouldNotBeNull().IsRead.ShouldBeTrue();

	[Then(@"^message (\d+) is not read here$")]
	public async Task NotReadHere(long id) =>
		(await world.HeldAsync(id)).ShouldNotBeNull().IsRead.ShouldBeFalse();

	[Then(@"^Fellowship was told message (\d+) was read$")]
	public void ServerTold(long id) => world.Fellowship.MarkedRead.ShouldContain(id);
}
