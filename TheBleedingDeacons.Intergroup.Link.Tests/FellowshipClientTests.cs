using System.Net;
using System.Text;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The REST client: the URLs it builds, the headers it sends, and — the
/// part that matters most — the difference it keeps between "the server
/// refused and said why" and "the request did not arrive".
/// </summary>
public sealed class FellowshipClientTests
{
	private static readonly FellowshipConfiguration Config = new()
	{
		BaseUrl = "https://aa-bristol.org",
		CallbackUrl = "link://auth",
	};

	[Fact]
	public void RoutesAreBuiltUnderFellowshipsOwnNamespace()
	{
		// The namespace is appended here rather than configured, so a
		// mis-set BaseUrl cannot point the app at some other plugin's
		// routes.
		Assert.Equal(
			"https://aa-bristol.org/wp-json/fellowship/v1/messages",
			Config.Route("messages").ToString());
	}

	[Fact]
	public void ATrailingSlashOnTheBaseUrlDoesNotDoubleUp()
	{
		var config = new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org/" };

		Assert.Equal(
			"https://aa-bristol.org/wp-json/fellowship/v1/directory",
			config.Route("directory").ToString());
	}

	[Fact]
	public async Task TheInboxIsReturnedStillSealed()
	{
		// The client never opens anything. Only the caller has the private
		// key, and putting the cipher in here would mean the client had to
		// be handed one.
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[{"id":9,"k":"d3JhcHBlZA==","p":"c2VhbGVk"}],"unread":4}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", sinceId: 3);

		Assert.True(page.Succeeded);
		Assert.Single(page.Messages);
		Assert.Equal(9, page.Messages[0].Id);
		Assert.Equal("d3JhcHBlZA==", page.Messages[0].WrappedKey);
		Assert.Equal("c2VhbGVk", page.Messages[0].Payload);
		Assert.Equal(4, page.Unread);
	}

	[Fact]
	public async Task ThePollAsksForWhatItDoesNotHaveYet()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"messages":[],"unread":0}""");

		await Client(handler).FetchInboxAsync("fdt_x", sinceId: 41);

		Assert.Contains("since=41", handler.LastUri?.Query, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheDeviceTokenTravelsAsABearerHeader()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"messages":[],"unread":0}""");

		await Client(handler).FetchInboxAsync("fdt_secret", sinceId: 0);

		Assert.Equal("Bearer", handler.LastAuthScheme);
		Assert.Equal("fdt_secret", handler.LastAuthParameter);
	}

	[Fact]
	public async Task AnEnvelopeMissingItsCiphertextIsDropped()
	{
		// Not an error — there is nothing the app could do with half an
		// envelope, and refusing the whole page would lose the good ones
		// alongside it.
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"messages":[{"id":1,"k":"","p":"c2VhbGVk"},{"id":2,"k":"aw==","p":"cA=="}],"unread":0}
			""");

		var page = await Client(handler).FetchInboxAsync("fdt_x", 0);

		Assert.Single(page.Messages);
		Assert.Equal(2, page.Messages[0].Id);
	}

	[Fact]
	public async Task ANetworkFailureIsNotAnEmptyInbox()
	{
		var handler = new ThrowingHandler(new HttpRequestException("no route to host"));

		var page = await Client(handler).FetchInboxAsync("fdt_x", 0);

		Assert.False(page.Succeeded);
	}

	[Fact]
	public async Task AServerErrorIsNotAnEmptyInboxEither()
	{
		var handler = new StubHandler(HttpStatusCode.InternalServerError, "{}");

		var page = await Client(handler).FetchInboxAsync("fdt_x", 0);

		Assert.False(page.Succeeded);
	}

	[Fact]
	public async Task ARefusedEnrolmentCarriesTheServersOwnWords()
	{
		// "That address does not match a member record" is the single most
		// common thing to go wrong here and the only one a member can act
		// on. Flattening it into "sign-in failed" throws away the only
		// useful thing on the screen.
		var handler = new StubHandler(HttpStatusCode.Forbidden, """
			{"code":"fellowship_not_a_member","message":"That address does not match a member record."}
			""");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			PublicKey = "spki",
			Platform = "android",
			Code = "abc",
		});

		Assert.False(result.Succeeded);
		Assert.Equal("That address does not match a member record.", result.Error);
	}

	[Fact]
	public async Task AnUnreachableServerSaysSoDifferently()
	{
		// Not the server's words, because there were none. A member should
		// be told to check their connection, not that they are not a
		// member.
		var handler = new ThrowingHandler(new HttpRequestException("dns"));

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			PublicKey = "spki",
			Platform = "android",
			Code = "abc",
		});

		Assert.False(result.Succeeded);
		Assert.Contains("connection", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ASuccessfulEnrolmentYieldsASession()
	{
		var handler = new StubHandler(HttpStatusCode.Created, """
			{"token":"fdt_abc","device":{"id":7},"member":{"id":31,"name":"Dave B"}}
			""");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			PublicKey = "spki",
			Platform = "android",
			Code = "code",
		});

		Assert.True(result.Succeeded);
		Assert.Equal("fdt_abc", result.Session!.Token);
		Assert.Equal(7, result.Session.DeviceId);
		Assert.Equal("Dave B", result.Session.MemberName);
	}

	[Fact]
	public async Task EnrolmentSendsOneCredentialShapeNotBoth()
	{
		// The server reads a request carrying an id_token for a
		// server-side provider as a wiring mistake, so an empty one must
		// not ride along beside a real code.
		var handler = new StubHandler(HttpStatusCode.Created, """{"token":"fdt_abc"}""");

		await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			PublicKey = "spki",
			Platform = "android",
			Code = "browser-code",
		});

		Assert.Contains("browser-code", handler.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("id_token", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheDirectoryComesBackAsNamesAndIdsOnly()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"members":[{"id":3,"name":"Dave B"},{"id":4,"name":"Sam T"}],
			 "committees":[{"slug":"pi","name":"Public Information","parent":0}]}
			""");

		var directory = await Client(handler).FetchDirectoryAsync("fdt_x");

		Assert.Equal(2, directory.Members.Count);
		Assert.Equal("Dave B", directory.Members[0].Name);
		Assert.Single(directory.Committees);
		Assert.Equal("pi", directory.Committees[0].Slug);
	}

	[Fact]
	public async Task ASendNamesMembersByIdAndNeverByAddress()
	{
		var handler = new StubHandler(HttpStatusCode.Created, """{"id":55,"recipients":2}""");

		var result = await Client(handler).SendAsync("fdt_x", new SendRequest
		{
			Subject = "Rota",
			Body = "Can you cover Thursday?",
			MemberIds = [3, 4],
		});

		Assert.True(result.Succeeded);
		Assert.Equal(55, result.MessageId);
		Assert.Equal(2, result.Recipients);
		Assert.Contains("member_ids", handler.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("@", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ARefusedSendCarriesTheServersReason()
	{
		var handler = new StubHandler(HttpStatusCode.BadRequest, """
			{"code":"fellowship_no_recipients","message":"That message would not reach anybody."}
			""");

		var result = await Client(handler).SendAsync("fdt_x", new SendRequest { Subject = "s", Body = "b" });

		Assert.False(result.Succeeded);
		Assert.Equal("That message would not reach anybody.", result.Error);
	}

	[Fact]
	public async Task SigningOutIsADeleteAndSurvivesAnUnreachableServer()
	{
		// The app clears its own token regardless, so a member is signed
		// out here even if the server never heard. The stale device row is
		// then revoked from the admin Devices screen.
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		Assert.False(await Client(handler).SignOutAsync("fdt_x"));
	}

	[Fact]
	public async Task StartingASignInPassesTheProviderAndTheCallback()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"state":"st","authorization_url":"https://accounts.google.com/o/oauth2/v2/auth?x=1"}
			""");

		var start = await Client(handler).StartSignInAsync("google");

		Assert.NotNull(start);
		Assert.True(start.IsBrowserFlow);
		Assert.Equal("st", start.State);
		Assert.Contains("provider=google", handler.LastUri?.Query, StringComparison.Ordinal);
		Assert.Contains("link%3A%2F%2Fauth", handler.LastUri?.Query, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AClientSideProviderAnswersANonceInsteadOfAUrl()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"state":"st","nonce":"n0nce"}""");

		var start = await Client(handler).StartSignInAsync("apple");

		Assert.NotNull(start);
		Assert.False(start.IsBrowserFlow);
		Assert.Equal("n0nce", start.Nonce);
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

	/// <summary>Stands in for a phone with no signal.</summary>
	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
	}
}
