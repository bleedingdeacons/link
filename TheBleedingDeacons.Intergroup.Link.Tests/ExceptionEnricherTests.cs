using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Intergroup.Link.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The enricher that flattens an exception into fields Better Stack can
/// filter on.
///
/// <para>The inner chain is the part worth testing. The interesting
/// failures on a duty handset are wrapped several layers deep — a
/// <c>TaskCanceledException</c> inside an <c>HttpRequestException</c>
/// inside whatever caught it — and everything diagnostic is behind the
/// outermost wrapper that the plain message shows.</para>
/// </summary>
public sealed class ExceptionEnricherTests
{
	private static Dictionary<string, string> Enrich(Exception? exception)
	{
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow,
			LogEventLevel.Error,
			exception,
			new MessageTemplateParser().Parse("Something failed"),
			[]);

		new ExceptionEnricher().Enrich(logEvent, new PropertyFactory());

		return logEvent.Properties.ToDictionary(
			p => p.Key,
			p => p.Value is ScalarValue { Value: string s } ? s : p.Value.ToString(),
			StringComparer.Ordinal);
	}

	[Fact]
	public void AddsNothingWhenThereIsNoException() => Assert.Empty(Enrich(null));

	[Fact]
	public void FlattensTheTypeAndMessage()
	{
		var properties = Enrich(new InvalidOperationException("audio focus refused"));

		Assert.Equal("System.InvalidOperationException", properties["ExceptionType"]);
		Assert.Equal("audio focus refused", properties["ExceptionMessage"]);
	}

	[Fact]
	public void RecordsAStackTraceWhenThereIsOne()
	{
		var properties = Enrich(Thrown(() => throw new InvalidOperationException("boom")));

		Assert.Contains("Thrown", properties["ExceptionStackTrace"], StringComparison.Ordinal);
	}

	[Fact]
	public void AddsNoInnerChainWhenThereIsNoInnerException() =>
		Assert.False(Enrich(new InvalidOperationException("alone")).ContainsKey("ExceptionInnerChain"));

	/// <summary>
	/// Everything diagnostic is usually behind the outermost wrapper, so
	/// the whole chain has to come through.
	/// </summary>
	[Fact]
	public void WalksTheWholeInnerChain()
	{
		var exception = new InvalidOperationException(
			"outer",
			new HttpRequestException(
				"middle",
				new TimeoutException("innermost")));

		var chain = Enrich(exception)["ExceptionInnerChain"];

		Assert.Contains("[1] System.Net.Http.HttpRequestException: middle", chain, StringComparison.Ordinal);
		Assert.Contains("[2] System.TimeoutException: innermost", chain, StringComparison.Ordinal);
	}

	/// <summary>
	/// An AggregateException is what a fire-and-forget loop produces, and
	/// its contents are the only useful part of it.
	/// </summary>
	[Fact]
	public void ExpandsEveryBranchOfAnAggregateException()
	{
		var chain = Enrich(new AggregateException(
			new TimeoutException("first"),
			new InvalidOperationException("second")))["ExceptionInnerChain"];

		Assert.Contains("System.TimeoutException: first", chain, StringComparison.Ordinal);
		Assert.Contains("System.InvalidOperationException: second", chain, StringComparison.Ordinal);
	}

	[Fact]
	public void FollowsAnAggregateBranchesOwnInnerException()
	{
		var chain = Enrich(new AggregateException(
			new InvalidOperationException("branch", new TimeoutException("beneath"))))["ExceptionInnerChain"];

		Assert.Contains("System.TimeoutException: beneath", chain, StringComparison.Ordinal);
	}

	/// <summary>
	/// A branch that appears twice is reported once. The guard is there
	/// because AggregateException flattening plus custom exception types
	/// have been known to produce loops, and a logging enricher that spins
	/// is worse than one that reports less.
	/// </summary>
	[Fact]
	public void ReportsARepeatedBranchOnce()
	{
		var shared = new TimeoutException("shared");

		var chain = Enrich(new AggregateException(shared, shared, new InvalidOperationException("other")))["ExceptionInnerChain"];

		Assert.Equal(
			1,
			chain.Split("System.TimeoutException: shared", StringSplitOptions.None).Length - 1);
		Assert.Contains("other", chain, StringComparison.Ordinal);
	}

	/// <summary>
	/// Nesting is bounded too, so an unbounded chain cannot turn one log
	/// line into an unbounded payload.
	/// </summary>
	[Fact]
	public void StopsAtTheInnerDepthLimit()
	{
		Exception nested = new TimeoutException("depth-0");
		for (var i = 1; i <= 15; i++)
		{
			nested = new InvalidOperationException($"depth-{i}", nested);
		}

		var chain = Enrich(nested)["ExceptionInnerChain"];

		Assert.Contains("inner exception depth limit reached", chain, StringComparison.Ordinal);
		Assert.DoesNotContain("depth-0", chain, StringComparison.Ordinal);
	}

	/// <summary>
	/// Deep async stacks are bounded rather than shipped whole — the
	/// payload goes over a metered connection from a phone.
	/// </summary>
	[Fact]
	public void TruncatesAStackTraceThatRunsAway()
	{
		var exception = new DeepStackException(string.Join('\n', Enumerable.Range(0, 200).Select(i => $"   at Frame{i}()")));

		var properties = Enrich(exception);

		Assert.Contains("more frames truncated", properties["ExceptionStackTrace"], StringComparison.Ordinal);
		Assert.Contains("at Frame39()", properties["ExceptionStackTrace"], StringComparison.Ordinal);
		Assert.DoesNotContain("at Frame40()", properties["ExceptionStackTrace"], StringComparison.Ordinal);
	}

	/// <summary>An existing property of the same name is not overwritten.</summary>
	[Fact]
	public void DoesNotOverwriteAPropertyThatIsAlreadyThere()
	{
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow,
			LogEventLevel.Error,
			new InvalidOperationException("boom"),
			new MessageTemplateParser().Parse("Something failed"),
			[new LogEventProperty("ExceptionType", new ScalarValue("already set"))]);

		new ExceptionEnricher().Enrich(logEvent, new PropertyFactory());

		Assert.Equal("already set", ((ScalarValue)logEvent.Properties["ExceptionType"]).Value);
	}

	private static Exception Thrown(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			return ex;
		}

		throw new InvalidOperationException("the action did not throw");
	}

	private sealed class PropertyFactory : ILogEventPropertyFactory
	{
		public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
			new(name, new ScalarValue(value));
	}

	private sealed class DeepStackException(string stack) : Exception("deep")
	{
		public override string? StackTrace => stack;
	}
}
