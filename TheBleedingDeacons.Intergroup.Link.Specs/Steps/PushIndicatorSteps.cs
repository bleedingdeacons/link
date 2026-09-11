using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// The push indicator: three inputs, four answers, and the words each one
/// gets.
/// </summary>
[Binding]
public sealed class PushIndicatorSteps(World world)
{
	[Given(@"^a phone whose build (carries|has no) push$")]
	public void Transport(string transport) =>
		world.Status = world.Status with
		{
			Supported = string.Equals(transport, "carries", StringComparison.Ordinal),
		};

	[Given(@"^whose owner has (left on|turned off) notifications$")]
	public void Permission(string permission) =>
		world.Status = world.Status with
		{
			Permitted = string.Equals(permission, "left on", StringComparison.Ordinal),
		};

	[Given(@"^which the intergroup (has a registration for|holds no registration for)$")]
	public void Registration(string registration) =>
		world.Status = world.Status with
		{
			Registered = registration.StartsWith("has", StringComparison.Ordinal),
		};

	[Given(@"^nothing has been read about push yet$")]
	public void Unknown() => world.Status = PushStatus.Unknown;

	/// <summary>
	/// A phone already in the named state, for the scenarios about what
	/// each state <i>says</i> rather than how it is arrived at.
	/// </summary>
	[Given(@"^push reads as (active|unregistered|blocked|unsupported)$")]
	public void InState(string state) => world.Status = state switch
	{
		"active" => new PushStatus(true, true, true),
		"unregistered" => new PushStatus(true, true, false),
		"blocked" => new PushStatus(true, false, true),
		_ => new PushStatus(false, true, true),
	};

	[Then(@"^push reads as (active|unregistered|blocked|unsupported)$")]
	public void ReadsAs(string state) =>
		world.Status.State.ShouldBe(Enum.Parse<PushState>(state, ignoreCase: true));

	[Then(@"^push is (working|not working)$")]
	public void IsWorking(string working) =>
		world.Status.IsActive.ShouldBe(string.Equals(working, "working", StringComparison.Ordinal));

	[Then(@"^the indicator is (#[0-9A-F]{6})$")]
	public void IndicatorColour(string colour) => world.Status.IndicatorColour.ShouldBe(colour);

	[Then(@"^it (needs|needs no) attention$")]
	public void NeedsAttention(string needs) =>
		world.Status.NeedsAttention.ShouldBe(string.Equals(needs, "needs", StringComparison.Ordinal));

	[Then(@"^its headline is ""(.+)""$")]
	public void Headline(string headline) => world.Status.Headline.ShouldBe(headline);

	[Then(@"^its detail mentions ""(.+)""$")]
	public void Detail(string words) => world.Status.Detail.ShouldContain(words);
}
