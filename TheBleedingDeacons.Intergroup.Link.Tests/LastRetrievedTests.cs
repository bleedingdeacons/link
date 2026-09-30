using TheBleedingDeacons.Intergroup.Link.Support;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The "config last retrieved" line under the build on Settings.
/// </summary>
public sealed class LastRetrievedTests
{
	[Fact]
	public void ATimeIsShownDayFirstToTheMinute()
	{
		var at = new DateTimeOffset(2026, 9, 30, 19, 57, 42, TimeSpan.FromHours(1));

		Assert.Equal("Config last retrieved 30/09/2026 19:57", LastRetrieved.Describe(at));
	}

	[Fact]
	public void NoTimeSaysItHasNotBeenRetrieved()
	{
		Assert.Equal("Config not retrieved yet", LastRetrieved.Describe(null));
	}
}
