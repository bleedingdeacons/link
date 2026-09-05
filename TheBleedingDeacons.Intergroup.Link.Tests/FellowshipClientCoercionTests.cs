using System.Net;
using System.Net.Sockets;
using System.Text;

using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Two things the client does on every response and that nothing was
/// covering: coercing the JSON the server actually sends, and deciding
/// whether an exception means "no signal" or something worse.
///
/// <para>Both are the seam between a PHP server and a C# client, which is
/// exactly where an untested branch does its damage — quietly, on a
/// handset, against a payload nobody wrote a fixture for.</para>
/// </summary>
public sealed class FellowshipClientCoercionTests
{
	private static readonly FellowshipConfiguration Config = new()
	{
		BaseUrl = "https://aa-bristol.org",
		CallbackUrl = "link://auth",
	};

	public static TheoryData<Exception> TransportFailures() =>
		new()
		{
			new HttpRequestException("no route to host"),

			// HttpClient raises this for a timeout as well as for a real
			// cancellation, which is why it is in the list at all.
			new TaskCanceledException("timed out"),
			new OperationCanceledException("cancelled"),
			new WebException("name resolution failed"),
			new IOException("the connection was reset"),

			// A socket failure arrives wrapped, so the classifier has to
			// match on the outer type rather than on the cause.
			new HttpRequestException("connect failed", new SocketException(10061)),
		};

	// ---------------------------------------------------------------
	// Numbers that arrive as strings.
	//
	// Not hypothetical. Fellowship reads its rows through wpdb, which
	// hands back every column as a PHP string, and json_encode then
	// emits them quoted. A route that has never been through an explicit
	// (int) cast on the server sends "id":"9", and the client has a
	// string-parse fallback for exactly that.
	//
	// It had no test at all: the fallback branch measured 0% covered, so
	// nothing would have noticed if it were deleted.
	// ---------------------------------------------------------------
	[Fact]
	public async Task AnIdSentAsAStringIsStillANumber()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[{"id":"9","k":"d3JhcHBlZA==","p":"c2VhbGVk"}],"unread":"4"}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.True(page.Succeeded);
		Assert.Equal(9, page.Messages[0].Id);
		Assert.Equal(4, page.Unread);
	}

	[Fact]
	public async Task AStringThatIsNotANumberReadsAsZeroAndDropsTheMessage()
	{
		// Zero is the sentinel the inbox loop already refuses on, and it
		// should: an id that could not be read is an id this app cannot
		// mark read or de-duplicate against, so showing the message would
		// mean showing it again on every poll forever.
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[{"id":"not a number","k":"a2V5","p":"cA=="}],"unread":"lots"}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.True(page.Succeeded);
		Assert.Empty(page.Messages);
		Assert.Equal(0, page.Unread);
	}

	[Fact]
	public async Task ANumberTooLargeForAnInt64DropsTheMessageToo()
	{
		// TryGetInt64 fails rather than wrapping, which is the arm that
		// keeps a nonsense value from becoming a plausible-looking one.
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[{"id":99999999999999999999999,"k":"a2V5","p":"cA=="}],"unread":0}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.True(page.Succeeded);
		Assert.Empty(page.Messages);
	}

	[Fact]
	public async Task OneUnreadableMessageDoesNotTakeTheRestOfThePageWithIt()
	{
		// The guarantee that makes dropping the right answer rather than
		// a silent loss: a page is filtered, never abandoned.
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[
				{"id":"nonsense","k":"a2V5","p":"cA=="},
				{"id":"11","k":"d3JhcHBlZA==","p":"c2VhbGVk"}
			],"unread":"1"}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.True(page.Succeeded);
		Assert.Equal(11, Assert.Single(page.Messages).Id);
		Assert.Equal(1, page.Unread);
	}

	[Fact]
	public async Task AMissingNumericFieldReadsAsZero()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"messages":[]}""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.True(page.Succeeded);
		Assert.Equal(0, page.Unread);
	}

	// ---------------------------------------------------------------
	// Text fields that are not strings.
	// ---------------------------------------------------------------
	[Fact]
	public async Task ATextFieldSentAsANumberIsReadAsItsDigits()
	{
		// The mirror of the case above, and it happens for the same
		// reason in reverse: a server that has started casting starts
		// sending a field this side declared as text as a bare number.
		var handler = new StubHandler(HttpStatusCode.Created, """
			{"token":"fdt_abc","member":{"id":3,"name":1934}}
			""");

		var result = await Client(handler).EnrolAsync(Enrolment());

		Assert.True(result.Succeeded);
		Assert.Equal("1934", result.Session?.MemberName);
	}

	[Fact]
	public async Task ATextFieldOfSomeOtherKindIsEmptyRatherThanNull()
	{
		// null, true and [] all land here. Empty string, not null: every
		// caller of MemberName treats it as a string and none of them
		// guard.
		var handler = new StubHandler(HttpStatusCode.Created, """
			{"token":"fdt_abc","member":{"id":3,"name":null}}
			""");

		var result = await Client(handler).EnrolAsync(Enrolment());

		Assert.True(result.Succeeded);
		Assert.Equal(string.Empty, result.Session?.MemberName);
	}

	// ---------------------------------------------------------------
	// Nested objects that are not objects.
	// ---------------------------------------------------------------
	[Fact]
	public async Task ANestedObjectSentAsSomethingElseIsTreatedAsAbsent()
	{
		// PHP's json_encode turns an empty associative array into [], not
		// {}, so "device":[] is what an unpopulated row looks like on the
		// wire. Reading it as absent is the difference between a session
		// with a zero device id and an exception during enrolment.
		var handler = new StubHandler(HttpStatusCode.Created, """
			{"token":"fdt_abc","device":[],"member":{"id":7,"name":"Ann"}}
			""");

		var result = await Client(handler).EnrolAsync(Enrolment());

		Assert.True(result.Succeeded);
		Assert.Equal(0, result.Session?.DeviceId);
		Assert.Equal(7, result.Session?.MemberId);
		Assert.Equal("Ann", result.Session?.MemberName);
	}

	// ---------------------------------------------------------------
	// What counts as "the request did not arrive".
	//
	// Only HttpRequestException was covered. The other arms are in the
	// classifier because HttpClient really does raise them, and the
	// negative case matters most: an exception that is not transport
	// must not be flattened into "check your connection", because that
	// sends a member to look at their signal over a bug in this app.
	// ---------------------------------------------------------------
	[Theory]
	[MemberData(nameof(TransportFailures))]
	public async Task EveryTransportFailureReadsAsAnUnreachableServer(Exception failure)
	{
		var page = await Client(new ThrowingHandler(failure)).FetchInboxAsync("fdt_x", sinceId: 0);

		Assert.False(page.Succeeded);
	}

	[Fact]
	public async Task AFailureThatIsNotTransportIsNotSwallowed()
	{
		// The whole point of the classifier. A bug in this app must
		// surface as a bug, not as "check your connection" — otherwise
		// the one report a member could usefully make is the one they
		// never make.
		var handler = new ThrowingHandler(new InvalidOperationException("a bug in this app"));

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => Client(handler).FetchInboxAsync("fdt_x", sinceId: 0));
	}

	// ---------------------------------------------------------------
	// Constructor guards.
	// ---------------------------------------------------------------
	[Fact]
	public void TheClientRefusesToBeBuiltWithoutAnHttpClient()
	{
		Assert.Throws<ArgumentNullException>(() => new FellowshipClient(null!, Config));
	}

	[Fact]
	public void TheClientRefusesToBeBuiltWithoutAConfiguration()
	{
		// Without this the failure is a NullReferenceException on the
		// first call, a long way from the registration that caused it.
		using var http = new HttpClient();

		Assert.Throws<ArgumentNullException>(() => new FellowshipClient(http, null!));
	}

	private static EnrolmentRequest Enrolment() => new()
	{
		PublicKey = "spki",
		Platform = "android",
		Code = "abc",
	};

	private static FellowshipClient Client(HttpMessageHandler handler) => new(new HttpClient(handler), Config);

	/// <summary>Answers one canned response.</summary>
	private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			});
	}

	/// <summary>Stands in for a phone with no signal.</summary>
	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
	}
}
