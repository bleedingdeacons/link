using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The Better Stack wire format.
///
/// <para>All of this exists because Serilog's stock JSON formatter emits
/// field names Better Stack does not recognise — most importantly it does
/// not emit <c>dt</c>, without which every event in a batch is stamped
/// with the moment the HTTP request arrived. On a handset that has
/// been out of signal for hours, that collapses a whole shift's
/// chronology onto one instant, which is the opposite of what the
/// diagnostics are for.</para>
/// </summary>
public sealed class BetterStackTextFormatterTests
{
	private static LogEvent Event(
		LogEventLevel level = LogEventLevel.Information,
		string template = "Message {MessageId} delivered",
		Exception? exception = null,
		params LogEventProperty[] properties)
	{
		var parsed = new MessageTemplateParser().Parse(template);
		return new LogEvent(
			new DateTimeOffset(2026, 8, 15, 21, 4, 31, TimeSpan.FromHours(1)),
			level,
			exception,
			parsed,
			properties);
	}

	private static JsonElement Format(LogEvent logEvent)
	{
		var writer = new StringWriter(CultureInfo.InvariantCulture);
		new BetterStackTextFormatter().Format(logEvent, writer);

		var line = writer.ToString().TrimEnd('\r', '\n');
		return JsonDocument.Parse(line).RootElement.Clone();
	}

	/// <summary>
	/// The timestamp must be the event's own, in UTC, in a shape Better
	/// Stack parses. This is the single most load-bearing assertion here.
	/// </summary>
	[Fact]
	public void StampsTheEventsOwnTimeInUtc()
	{
		var json = Format(Event());

		Assert.Equal("2026-08-15T20:04:31.0000000Z", json.GetProperty("dt").GetString());
	}

	/// <summary>
	/// Serilog's level names are not all names Better Stack recognises.
	/// </summary>
	[Theory]
	[InlineData(LogEventLevel.Verbose, "TRACE")]
	[InlineData(LogEventLevel.Debug, "DEBUG")]
	[InlineData(LogEventLevel.Information, "INFO")]
	[InlineData(LogEventLevel.Warning, "WARN")]
	[InlineData(LogEventLevel.Error, "ERROR")]
	[InlineData(LogEventLevel.Fatal, "FATAL")]
	public void MapsSerilogsLevelNamesOntoTheOnesBetterStackUses(LogEventLevel level, string expected) =>
		Assert.Equal(expected, Format(Event(level)).GetProperty("level").GetString());

	[Fact]
	public void WritesTheRenderedMessageAndTheTemplateItCameFrom()
	{
		var json = Format(Event(
			template: "Message {MessageId} delivered",
			properties: new LogEventProperty("MessageId", new ScalarValue(4242))));

		Assert.Equal("Message 4242 delivered", json.GetProperty("message").GetString());
		Assert.Equal("Message {MessageId} delivered", json.GetProperty("messageTemplate").GetString());
	}

	[Fact]
	public void NestsEnrichedPropertiesSoTheyCannotShadowTheReservedFields()
	{
		var json = Format(Event(
			template: "Message {MessageId} delivered",
			properties:
			[
				new LogEventProperty("MessageId", new ScalarValue(4242)),
				new LogEventProperty("level", new ScalarValue("not-a-level")),
				new LogEventProperty("dt", new ScalarValue("not-a-time")),
			]));

		Assert.Equal("INFO", json.GetProperty("level").GetString());
		Assert.Equal("2026-08-15T20:04:31.0000000Z", json.GetProperty("dt").GetString());
		Assert.Equal("not-a-level", json.GetProperty("properties").GetProperty("level").GetString());
	}

	[Fact]
	public void OmitsThePropertiesObjectWhenThereAreNone() =>
		Assert.False(Format(Event(template: "Nothing to say")).TryGetProperty("properties", out _));

	[Fact]
	public void IncludesTheExceptionWhenThereIsOne()
	{
		var json = Format(Event(exception: new InvalidOperationException("audio focus refused")));

		Assert.Contains("audio focus refused", json.GetProperty("exception").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void OmitsTheExceptionWhenThereIsNot() =>
		Assert.False(Format(Event()).TryGetProperty("exception", out _));

	/// <summary>Every event is one line — the buffer file is read back a line at a time.</summary>
	[Fact]
	public void WritesExactlyOneLinePerEvent()
	{
		var writer = new StringWriter(CultureInfo.InvariantCulture);
		var formatter = new BetterStackTextFormatter();

		formatter.Format(Event(), writer);
		formatter.Format(Event(), writer);

		var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.All(lines, line => JsonDocument.Parse(line.Trim()));
	}

	/// <summary>
	/// A message containing quotes and newlines has to survive as valid
	/// JSON, or the row it lands on is unreadable.
	/// </summary>
	[Fact]
	public void EscapesAwkwardText()
	{
		var json = Format(Event(
			template: "Said {What}",
			properties: new LogEventProperty("What", new ScalarValue("a \"quote\"\nand a newline"))));

		// Asserting on the parsed value rather than the escaped line: what has
		// to hold is that the line is valid JSON — JsonDocument.Parse in
		// Format would have thrown otherwise — and that the awkward
		// characters came back out of it intact. How Serilog chose to render
		// the scalar on the way in is its business.
		var message = json.GetProperty("message").GetString()!;
		Assert.Contains("quote", message, StringComparison.Ordinal);
		Assert.Contains('"', message);
		Assert.Contains('\n', message);
		Assert.Contains("and a newline", message, StringComparison.Ordinal);
	}

	[Fact]
	public void RejectsNulls()
	{
		var formatter = new BetterStackTextFormatter();

		Assert.Throws<ArgumentNullException>(() => formatter.Format(null!, new StringWriter()));
		Assert.Throws<ArgumentNullException>(() => formatter.Format(Event(), null!));
	}

	/// <summary>
	/// Dropping one event beats taking down the shipper loop, so a writer
	/// that fails must not propagate.
	/// </summary>
	[Fact]
	public void DropsAnEventItCannotWriteRatherThanThrowing()
	{
		new BetterStackTextFormatter().Format(Event(), new ThrowingWriter());
	}

	private sealed class ThrowingWriter : StringWriter
	{
		public override void WriteLine(string? value) => throw new IOException("disk full");
	}
}

/// <summary>
/// The batch framing: one JSON object per line, no outer array.
/// </summary>
public sealed class BetterStackNdjsonBatchFormatterTests
{
	[Fact]
	public void PassesBufferedEventsThroughOnePerLine()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format(["""{"dt":"a"}""", """{"dt":"b"}"""], output);

		Assert.Equal(
			["""{"dt":"a"}""", """{"dt":"b"}"""],
			output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
	}

	/// <summary>
	/// A blank row would be a body line Better Stack cannot parse, for no
	/// event at all.
	/// </summary>
	[Fact]
	public void SkipsBlankRows()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format(["""{"dt":"a"}""", "", "   ", null!], output);

		Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public void WritesNothingForANullBatch()
	{
		var output = new StringWriter();

		new BetterStackNdjsonBatchFormatter().Format((IEnumerable<string>)null!, output);

		Assert.Equal(string.Empty, output.ToString());
	}

	/// <summary>
	/// The unbuffered overload is not what the durable sink calls, but it
	/// has to frame events to the same one-per-line rule — the configured
	/// formatter may or may not terminate the event itself.
	/// </summary>
	[Fact]
	public void NormalisesLineEndingsInTheUnbufferedOverload()
	{
		var output = new StringWriter();
		var logEvent = new LogEvent(
			DateTimeOffset.UtcNow, LogEventLevel.Information, null,
			new MessageTemplateParser().Parse("Hello"), []);

		new BetterStackNdjsonBatchFormatter().Format([logEvent, logEvent], new BetterStackTextFormatter(), output);

		var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Equal(2, lines.Length);
		Assert.All(lines, line => JsonDocument.Parse(line.Trim()));
	}

	[Fact]
	public void WritesNothingWhenTheUnbufferedOverloadHasNothingToWorkWith()
	{
		var output = new StringWriter();
		var formatter = new BetterStackNdjsonBatchFormatter();

		formatter.Format(null!, new BetterStackTextFormatter(), output);
		formatter.Format([], null!, output);

		Assert.Equal(string.Empty, output.ToString());
	}
}

/// <summary>
/// The transport underneath the durable sink.
///
/// <para>Its one absolute requirement is that it never throws. The sink
/// calls it from a background shipper loop <i>and</i> from its dispose
/// path, and an exception out of the latter propagates into application
/// shutdown.</para>
/// </summary>
public sealed class BetterStackHttpClientTests
{
	private static (BetterStackHttpClient Client, StubHttpMessageHandler Handler) Build(
		HttpStatusCode status = HttpStatusCode.Accepted)
	{
		var handler = StubHttpMessageHandler.Always(status, "{}");
		return (new BetterStackHttpClient("src-token", new HttpClient(handler)), handler);
	}

	private static Stream Batch(string ndjson) => new MemoryStream(Encoding.UTF8.GetBytes(ndjson));

	[Fact]
	public void RejectsItsDependenciesBeingNull()
	{
		Assert.Throws<ArgumentNullException>(() => new BetterStackHttpClient(null!, new HttpClient()));
		Assert.Throws<ArgumentNullException>(() => new BetterStackHttpClient("t", null!));
	}

	[Fact]
	public async Task PostsTheBatchAsNdjsonUnderTheSourceToken()
	{
		var (client, handler) = Build();

		var response = await client.PostAsync(
			"https://s1.betterstackdata.com", Batch("""{"dt":"a"}"""), CancellationToken.None);

		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
		Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
		Assert.Equal("src-token", handler.Requests[0].Headers.Authorization!.Parameter);
		Assert.Equal("application/x-ndjson", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
		Assert.Equal("""{"dt":"a"}""", handler.Bodies[0]);
	}

	/// <summary>
	/// A non-2xx tells the sink to keep the batch on disk and try again,
	/// which is exactly what a transport failure should produce — so the
	/// failure is mapped to a status rather than thrown.
	/// </summary>
	[Fact]
	public async Task TurnsATransportFailureIntoARetryableResponse()
	{
		var client = new BetterStackHttpClient(
			"src-token",
			new HttpClient(new ThrowingHandler(new HttpRequestException("no route to host"))));

		var response = await client.PostAsync(
			"https://s1.betterstackdata.com", Batch("{}"), CancellationToken.None);

		Assert.Equal(599, (int)response.StatusCode);
		Assert.False(response.IsSuccessStatusCode);
		Assert.Equal("no route to host", response.ReasonPhrase);
	}

	/// <summary>
	/// Shutdown cancels the shipper. The batch must be retained, and
	/// nothing may escape into the dispose path.
	/// </summary>
	[Fact]
	public async Task RetainsTheBatchWhenTheShipperIsCancelled()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var client = new BetterStackHttpClient(
			"src-token",
			new HttpClient(new ThrowingHandler(new OperationCanceledException())));

		var response = await client.PostAsync("https://s1.betterstackdata.com", Batch("{}"), cts.Token);

		Assert.Equal(599, (int)response.StatusCode);
	}

	/// <summary>
	/// The HttpClient is the app-wide singleton. Disposing it here would
	/// kill every other outbound call the app makes.
	/// </summary>
	[Fact]
	public async Task DoesNotDisposeTheSharedHttpClient()
	{
		var handler = StubHttpMessageHandler.Always(HttpStatusCode.Accepted, "{}");
		var httpClient = new HttpClient(handler);
		var client = new BetterStackHttpClient("src-token", httpClient);

		client.Dispose();

		var response = await httpClient.GetAsync(new Uri("https://s1.betterstackdata.com"));
		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
	}

	/// <summary>Configuration comes from the constructor, so this is a no-op.</summary>
	[Fact]
	public void ConfigureDoesNothing()
	{
		var (client, _) = Build();

		client.Configure(null!);
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw exception;
	}

	/// <summary>
	/// Records what was posted and answers with a fixed status.
	///
	/// <para>Nested here rather than shared, which is this project's habit
	/// for HTTP stubs — the client tests each carry their own. This one
	/// keeps the requests and their bodies, because what the sink puts on
	/// the wire is the whole point of these tests.</para>
	/// </summary>
	private sealed class StubHttpMessageHandler(
		Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder)
		: HttpMessageHandler
	{
		/// <summary>Every request that reached the handler, in order.</summary>
		public List<HttpRequestMessage> Requests { get; } = [];

		/// <summary>The bodies of those requests, in the same order.</summary>
		public List<string> Bodies { get; } = [];

		public static StubHttpMessageHandler Always(HttpStatusCode status, string body) =>
			new(_ => (status, body));

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			Bodies.Add(request.Content is null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

			var (status, body) = responder(request);

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
				RequestMessage = request,
			};
		}
	}
}
