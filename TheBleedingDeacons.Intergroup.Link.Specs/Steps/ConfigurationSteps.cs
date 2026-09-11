using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>Where this build talks to, and what it builds out of that.</summary>
[Binding]
public sealed class ConfigurationSteps(World world)
{
	[Given(@"^a server address of ""(.*)""$")]
	public void AnAddress(string address) => world.Settings = new FellowshipConfiguration { BaseUrl = address };

	[Given(@"^a build nobody has configured$")]
	public void Untouched() => world.Settings = new FellowshipConfiguration();

	[Then(@"^it (is|is not) somewhere Link will talk to$")]
	public void Verdict(string verdict) =>
		world.Settings.ShouldNotBeNull().IsConfigured
			.ShouldBe(string.Equals(verdict, "is", StringComparison.Ordinal));

	[Then(@"^the route for ""(.+)"" is ""(.+)""$")]
	public void Route(string path, string expected) =>
		world.Settings.ShouldNotBeNull().Route(path).ToString().ShouldBe(expected);

	[Then(@"^the callback is ""(.+)""$")]
	public void Callback(string callback) =>
		world.Settings.ShouldNotBeNull().CallbackUrl.ShouldBe(callback);
}
