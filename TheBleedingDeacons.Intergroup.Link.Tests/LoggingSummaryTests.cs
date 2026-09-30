using TheBleedingDeacons.Intergroup.Link.Support;
using TheBleedingDeacons.Inventory;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The Better Stack lines under the build on Settings. The one rule that
/// matters is that the token itself never reaches the screen.
/// </summary>
public sealed class LoggingSummaryTests
{
	private static string[] Lines(BetterStackConfiguration? configuration, ShippingState state) =>
		LoggingSummary.Describe(configuration, state).Split(Environment.NewLine);

	[Fact]
	public void Shipping_names_the_endpoint_and_says_a_token_is_set()
	{
		var lines = Lines(
			new BetterStackConfiguration { Endpoint = "https://s1.betterstackdata.com", SourceToken = "src-token" },
			ShippingState.Shipping);

		Assert.Equal(["Better Stack: shipping", "https://s1.betterstackdata.com", "Source token set"], lines);
	}

	[Fact]
	public void The_token_itself_is_never_shown()
	{
		var summary = LoggingSummary.Describe(
			new BetterStackConfiguration { Endpoint = "https://s1.betterstackdata.com", SourceToken = "src-token" },
			ShippingState.Shipping);

		Assert.DoesNotContain("src-token", summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Nothing_stored_says_so_on_every_line()
	{
		Assert.Equal(
			["Better Stack: holding logs until it is told where to ship", "No endpoint", "No source token"],
			Lines(null, ShippingState.Holding));
	}

	[Fact]
	public void An_endpoint_without_a_token_keeps_logs_on_the_phone()
	{
		Assert.Equal(
			["Better Stack: logs stay on this phone", "https://s1.betterstackdata.com", "No source token"],
			Lines(new BetterStackConfiguration { Endpoint = "s1.betterstackdata.com" }, ShippingState.LocalOnly));
	}

	[Fact]
	public void Before_the_shipper_starts_it_says_not_started()
	{
		Assert.Equal("Better Stack: not started", Lines(null, ShippingState.NotStarted)[0]);
	}
}
