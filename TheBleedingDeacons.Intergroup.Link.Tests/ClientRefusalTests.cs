using System.Net;
using System.Text;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The client's remaining refusals, and the shapes a send can take.
///
/// <para>Everything here is a path the app takes when the server answers
/// something it did not expect. They are worth pinning because each one
/// currently ends in a sentence a member reads, and the difference
/// between them is the difference between "try again" and "tell your
/// secretary".</para>
/// </summary>
public sealed class ClientRefusalTests
{
	private static readonly FellowshipConfiguration Config = new()
	{
		BaseUrl = "https://aa-bristol.org",
		CallbackUrl = "link://auth",
	};

	[Fact]
	public async Task AStartWithNoStateIsNotAStart()
	{
		// The state ties the rest of the flow to this attempt. Without it
		// there is nothing to send back, so proceeding would produce a
		// browser leg whose callback could never be matched.
		var handler = new StubHandler(HttpStatusCode.OK, """{"authorization_url":"https://example.org"}""");

		Assert.Null(await Client(handler).StartSignInAsync("google"));
	}

	[Fact]
	public async Task AStartTheServerRefusedIsNotAStart()
	{
		var handler = new StubHandler(HttpStatusCode.BadRequest, """{"message":"Unknown sign-in provider."}""");

		Assert.Null(await Client(handler).StartSignInAsync("myspace"));
	}

	[Fact]
	public async Task AnEnrolmentWithNoTokenIsRefusedEvenOnA201()
	{
		// A 201 with no token would otherwise become a session that
		// authenticates nothing, and the failure would surface later as
		// every subsequent call being rejected.
		var handler = new StubHandler(HttpStatusCode.Created, """{"device":{"id":4}}""");

		var result = await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			Code = "abc",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.False(result.Succeeded);
		Assert.Contains("device token", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task AnOAuthEnrolmentSendsTheStateAndTokenNotACode()
	{
		// The Apple shape. Its counterpart — code, no state — is covered
		// in FellowshipClientTests; this is the other branch.
		var handler = new StubHandler(HttpStatusCode.Created, """{"token":"fdt_abc"}""");

		await Client(handler).EnrolAsync(new EnrolmentRequest
		{
			State = "st",
			IdToken = "eyJ.header.sig",
			PublicKey = "spki",
			Platform = "ios",
		});

		Assert.Contains("\"state\":\"st\"", handler.LastBody, StringComparison.Ordinal);
		Assert.Contains("\"id_token\"", handler.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("\"code\"", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ASendToACommitteeNamesTheCommitteeAndNoMembers()
	{
		var handler = new StubHandler(HttpStatusCode.Created, """{"id":9}""");

		await Client(handler).SendAsync("fdt_x", new SendRequest
		{
			Subject = "Intergroup moved",
			Body = "Now the 14th.",
			Committee = "steering",
		});

		Assert.Contains("\"committee\":\"steering\"", handler.LastBody, StringComparison.Ordinal);
		Assert.DoesNotContain("member_ids", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AReplyCarriesWhatItAnswers()
	{
		// reply_to is how a thread is derived later; a reply that loses it
		// is an ordinary message nobody can trace back.
		var handler = new StubHandler(HttpStatusCode.Created, """{"id":9}""");

		await Client(handler).SendAsync("fdt_x", new SendRequest
		{
			Subject = "Re: Intergroup moved",
			Body = "Noted.",
			Committee = "steering",
			ReplyToId = 42,
		});

		Assert.Contains("\"reply_to\":42", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ASendWithNoReplyOmitsTheFieldRatherThanSendingZero()
	{
		// A zero would be a reply to message 0, which is not a message.
		var handler = new StubHandler(HttpStatusCode.Created, """{"id":9}""");

		await Client(handler).SendAsync("fdt_x", new SendRequest
		{
			Subject = "Fresh",
			Body = "Not a reply.",
			Committee = "steering",
		});

		Assert.DoesNotContain("reply_to", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnUnreachableServerLeavesAMessageUnsent()
	{
		// Worth its own wording: "not sent" is actionable in a way that
		// "failed" is not, because the member still has the text.
		var handler = new ThrowingHandler(new HttpRequestException("offline"));

		var result = await Client(handler).SendAsync("fdt_x", new SendRequest
		{
			Subject = "Intergroup moved",
			Body = "Now the 14th.",
			Committee = "steering",
		});

		Assert.False(result.Succeeded);
		Assert.Contains("not been sent", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	private static FellowshipClient Client(HttpMessageHandler handler) => new(new HttpClient(handler), Config);

	private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		public string LastBody { get; private set; } = string.Empty;

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
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
