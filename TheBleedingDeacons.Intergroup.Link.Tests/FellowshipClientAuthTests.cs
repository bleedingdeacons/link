using System.Net;
using System.Text;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The rest of the REST client: the password flow, and the four small
/// endpoints a handset calls about itself.
///
/// <para><b>Separate from FellowshipClientTests because the concern is
/// different.</b> That class is about messages — what the app asks for
/// and what it does with the answer. This one is about a handset's own
/// account of itself: its password, its push token, its key, and the
/// admission that it can no longer read anything.</para>
///
/// <para>What is asserted throughout is the distinction the client exists
/// to keep: <b>"the server refused and said why" is not the same as "the
/// request never arrived"</b>. Collapsing those two is how a member gets
/// told their password is wrong when the wifi is off.</para>
/// </summary>
public sealed class FellowshipClientAuthTests
{
	private static readonly FellowshipConfiguration Config = new()
	{
		BaseUrl = "https://aa-bristol.org",
		CallbackUrl = "link://auth",
	};

	// ── Signing in with a password ────────────────────────────────────

	[Fact]
	public async Task APasswordSignInPostsToItsOwnRouteNotTheExchange()
	{
		// A different route rather than a third branch of the exchange:
		// it is the only shape where the app hands over a secret rather
		// than relaying somebody else's proof, and the server rate-limits
		// and audits it accordingly.
		var handler = new StubHandler(HttpStatusCode.Created, """
			{"token":"fdt_abc","device":{"id":4},"member":{"id":7,"name":"Dave P"}}
			""");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			Email = "member@example.org",
			Password = "correct horse battery staple",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.True(result.Succeeded);
		Assert.EndsWith("auth/device/password", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
		Assert.Contains("\"email\":\"member@example.org\"", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task APasswordSignInSendsNoCodeAndNoIdToken()
	{
		// The server refuses a request carrying two credential shapes, and
		// an empty one alongside a real one looks to it like the wrong
		// flow.
		var handler = new StubHandler(HttpStatusCode.Created, """{"token":"fdt_abc"}""");

		await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			Email = "member@example.org",
			Password = "a passphrase",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.DoesNotContain("\"code\"", handler.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("\"id_token\"", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WrongCredentialsCarryTheServersOwnWording()
	{
		// One message for an unknown address, no password, a wrong
		// password and a locked account — the server says so deliberately,
		// and the app must not try to be more specific than it was.
		var handler = new StubHandler(HttpStatusCode.Unauthorized, """
			{"message":"Email or password is incorrect."}
			""");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			Email = "member@example.org",
			Password = "wrong",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.False(result.Succeeded);
		Assert.Equal("Email or password is incorrect.", result.Error);
	}

	// ── Asking for a code ─────────────────────────────────────────────

	[Fact]
	public async Task RequestingACodeAnswersTrueWhateverTheServerKnows()
	{
		// The server answers 200 for a member, a non-member and a stranger
		// alike. True here means "the request arrived", never "that
		// address exists" — the app must not turn a reachable server into
		// a membership oracle.
		var handler = new StubHandler(HttpStatusCode.OK, """{"sent":true}""");

		Assert.True(await Client(handler).RequestPasswordLinkAsync("anybody@example.org"));
		Assert.EndsWith("auth/password/request", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RequestingACodeAnswersFalseOnlyWhenTheServerWasNotReached()
	{
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		Assert.False(await Client(handler).RequestPasswordLinkAsync("member@example.org"));
	}

	// ── Setting the password ──────────────────────────────────────────

	[Fact]
	public async Task SettingAPasswordSendsTheCodeAndTheNewSecret()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true}""");

		var result = await Client(handler).SetPasswordAsync("the-code", "a brand new passphrase");

		Assert.True(result.Succeeded);
		Assert.EndsWith("auth/password/complete", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
		Assert.Contains("\"token\":\"the-code\"", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnExpiredCodeIsReportedInTheServersWords()
	{
		var handler = new StubHandler(HttpStatusCode.BadRequest, """
			{"message":"That link has expired or has already been used. Please ask for a new one."}
			""");

		var result = await Client(handler).SetPasswordAsync("stale", "a passphrase");

		Assert.False(result.Succeeded);
		Assert.Contains("expired", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AWeakPasswordIsReportedInTheServersWords()
	{
		// 422, not 400: the code was good and stays usable, so the member
		// can try a different password without asking for another email.
		// The app's only job is to repeat the reason.
		var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, """
			{"message":"Please use at least 14 characters."}
			""");

		var result = await Client(handler).SetPasswordAsync("the-code", "short");

		Assert.False(result.Succeeded);
		Assert.Contains("14 characters", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ARefusalWithNoMessageStillSaysSomethingUseful()
	{
		// A bare status with no body would otherwise reach the screen as
		// an empty string, which reads as nothing having happened.
		var handler = new StubHandler(HttpStatusCode.BadRequest, "");

		var result = await Client(handler).SetPasswordAsync("the-code", "a passphrase");

		Assert.False(result.Succeeded);
		Assert.NotEmpty(result.Error);
	}

	[Fact]
	public async Task AnUnreachableServerIsNotAWrongCode()
	{
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		var result = await Client(handler).SetPasswordAsync("the-code", "a passphrase");

		Assert.False(result.Succeeded);
		Assert.Contains("connection", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	// ── What a handset says about itself ──────────────────────────────

	[Fact]
	public async Task ThePushTokenIsSentAsAnFcmRegistration()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true}""");

		Assert.True(await Client(handler).UpdatePushTokenAsync("fdt_x", "fcm-token-1"));

		Assert.EndsWith("auth/device/push", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
		Assert.Contains("\"push_provider\":\"fcm\"", handler.LastBody, StringComparison.Ordinal);
		Assert.Equal("Bearer", handler.LastAuthScheme);
	}

	[Fact]
	public async Task AFailedPushRegistrationIsReportedRatherThanThrown()
	{
		// The launch backstop calls this and ignores the answer; a throw
		// here would take out App.OnStart instead.
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		Assert.False(await Client(handler).UpdatePushTokenAsync("fdt_x", "fcm-token-1"));
	}

	[Fact]
	public async Task ARotatedKeyIsPresentedToTheServer()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true}""");

		Assert.True(await Client(handler).RotateKeyAsync("fdt_x", "new-spki"));

		Assert.EndsWith("auth/device/key", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
		Assert.Contains("\"public_key\":\"new-spki\"", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AKeyFaultIsReportedWithNoBodyAtAll()
	{
		// Nothing to say beyond "this handset cannot open its messages".
		// The server has the device from the bearer token.
		var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true}""");

		Assert.True(await Client(handler).ReportKeyFaultAsync("fdt_x"));

		Assert.EndsWith("auth/device/key-fault", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
		Assert.Equal("fdt_x", handler.LastAuthParameter);
	}

	[Fact]
	public async Task AKeyFaultThatCannotBeReportedIsNotAnException()
	{
		// Raised from a sync path that must not throw; the next sync tries
		// again.
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		Assert.False(await Client(handler).ReportKeyFaultAsync("fdt_x"));
	}

	[Fact]
	public async Task MarkingAMessageReadNamesItInTheRoute()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"ok":true}""");

		Assert.True(await Client(handler).MarkReadAsync("fdt_x", 42));

		Assert.EndsWith("messages/42/read", handler.LastUri?.AbsolutePath, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AServerErrorLeavesAMessageUnread()
	{
		// False rather than an exception: the history keeps its own read
		// flag and the next sync reconciles.
		var handler = new StubHandler(HttpStatusCode.InternalServerError, "");

		Assert.False(await Client(handler).MarkReadAsync("fdt_x", 42));
	}

	[Fact]
	public async Task AnAnswerThatIsNotJsonIsTreatedAsNoAnswer()
	{
		// A proxy or a captive portal returning HTML with a 200 is the
		// realistic case. Parsing it as success would produce a session
		// with no token.
		var handler = new StubHandler(HttpStatusCode.Created, "<html>hello</html>");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			Code = "abc",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.False(result.Succeeded);
		Assert.NotEmpty(result.Error);
	}

	private static FellowshipClient Client(HttpMessageHandler handler) => new(new HttpClient(handler), Config);

	/// <summary>Answers one canned response and records what it was asked.</summary>
	private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		public Uri? LastUri { get; private set; }

		public string? LastAuthScheme { get; private set; }

		public string? LastAuthParameter { get; private set; }

		public string LastBody { get; private set; } = string.Empty;

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			LastUri = request.RequestUri;
			LastAuthScheme = request.Headers.Authorization?.Scheme;
			LastAuthParameter = request.Headers.Authorization?.Parameter;

			if (request.Content is not null)
			{
				LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			}

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
		}
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
	}
}
