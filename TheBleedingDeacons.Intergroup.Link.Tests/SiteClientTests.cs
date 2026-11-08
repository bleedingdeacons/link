using System.Net;
using System.Text;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What stays in WordPress: the address book and setting a password.
///
/// <para>The directory is read leniently, because a directory that failed to parse would
/// take the whole address book with it.</para>
/// </summary>
public sealed class SiteClientTests
{
	private static readonly FellowshipConfiguration Config = new() { BaseUrl = "https://aa-bristol.org/amber" };

	[Fact]
	public async Task TheDirectoryIsAskedForWithTheDevicesToken()
	{
		var handler = new Stub(HttpStatusCode.OK, """{"members":[]}""");

		await Client(handler).FetchDirectoryAsync("id-token");

		Assert.Equal("https://aa-bristol.org/amber/wp-json/fellowship/v1/directory", handler.Url);
		Assert.Equal("Bearer id-token", handler.Authorization);
	}

	[Fact]
	public async Task TheDirectoryReadsEveryField()
	{
		var directory = await Client(new Stub(HttpStatusCode.OK, """
			{
				"members": [{"id":7,"name":"Dave P","group":"Bristol","gsr":true,"position":"Chair","hasDevice":true}],
				"committees": [{"slug":"steering","name":"Steering","parent":3}],
				"gsrs": 12
			}
			""")).FetchDirectoryAsync("t");

		var member = Assert.Single(directory.Members);
		Assert.Equal(7, member.Id);
		Assert.Equal("Dave P", member.Name);
		Assert.Equal("Bristol", member.HomeGroup);
		Assert.True(member.IsGsr);
		Assert.Equal("Chair", member.Position);
		Assert.True(member.HasDevice);

		var committee = Assert.Single(directory.Committees);
		Assert.Equal("steering", committee.Slug);
		Assert.Equal(3, committee.ParentId);
		Assert.Equal(12, directory.GsrCount);
	}

	[Theory]
	[InlineData("""{"id":7,"name":"A"}""", true)]
	[InlineData("""{"id":7,"name":"A","hasDevice":false}""", false)]
	[InlineData("""{"id":7,"name":"A","hasDevice":0}""", false)]
	[InlineData("""{"id":7,"name":"A","hasDevice":"1"}""", true)]
	public async Task NotBeingToldAboutADeviceMeansSelectable(string member, bool expected)
	{
		var directory = await Client(new Stub(HttpStatusCode.OK, $$"""{"members":[{{member}}]}""")).FetchDirectoryAsync("t");

		Assert.Equal(expected, Assert.Single(directory.Members).HasDevice);
	}

	[Theory]
	[InlineData("""{"id":"7","name":"Text id"}""", 1)]
	[InlineData("""{"id":0,"name":"No id"}""", 0)]
	[InlineData("""{"name":"Missing id"}""", 0)]
	public async Task AMemberWithNoUsableIdIsLeftOut(string member, int expected)
	{
		var directory = await Client(new Stub(HttpStatusCode.OK, $$"""{"members":[{{member}}]}""")).FetchDirectoryAsync("t");

		Assert.Equal(expected, directory.Members.Count);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, "{}")]
	[InlineData(HttpStatusCode.ServiceUnavailable, "<html>")]
	[InlineData(HttpStatusCode.OK, "not json")]
	[InlineData(HttpStatusCode.OK, "[]")]
	public async Task ADirectoryThatCannotBeReadIsEmpty(HttpStatusCode status, string body)
	{
		var directory = await Client(new Stub(status, body)).FetchDirectoryAsync("t");

		Assert.Empty(directory.Members);
	}

	[Fact]
	public async Task NoNetworkIsAnEmptyDirectoryRatherThanAnException()
	{
		var directory = await Client(new Stub(new HttpRequestException("offline"))).FetchDirectoryAsync("t");

		Assert.Empty(directory.Members);
	}

	[Theory]
	[InlineData(HttpStatusCode.OK, true)]
	[InlineData(HttpStatusCode.ServiceUnavailable, false)]
	public async Task AskingForAPasswordLinkSaysOnlyWhetherItArrived(HttpStatusCode status, bool expected)
	{
		var handler = new Stub(status, "{}");

		Assert.Equal(expected, await Client(handler).RequestPasswordLinkAsync("a@example.org"));
		Assert.EndsWith("/auth/password/request", handler.Url, StringComparison.Ordinal);
		Assert.Contains("a@example.org", handler.Body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task APasswordIsSetWithTheCode()
	{
		var handler = new Stub(HttpStatusCode.OK, "{}");

		var result = await Client(handler).SetPasswordAsync("the-code", "a long password");

		Assert.True(result.Succeeded);
		Assert.EndsWith("/auth/password/complete", handler.Url, StringComparison.Ordinal);
		Assert.Contains("the-code", handler.Body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ARefusedPasswordSaysWhatTheSiteSaid()
	{
		var result = await Client(new Stub(HttpStatusCode.BadRequest, """{"message":"Please use at least 14 characters."}"""))
			.SetPasswordAsync("code", "short");

		Assert.Equal("Please use at least 14 characters.", result.Error);
	}

	[Fact]
	public async Task NoNetworkWhileSettingAPasswordSaysSo()
	{
		var result = await Client(new Stub(new HttpRequestException("offline"))).SetPasswordAsync("code", "password");

		Assert.False(result.Succeeded);
		Assert.Contains("Could not reach", result.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AMemberIsVouchedForOnTheStrengthOfTheirSignIn()
	{
		var handler = new Stub(HttpStatusCode.OK, """{"attestation":"signed.member.statement"}""");

		var attestation = await Client(handler).AttestMemberAsync("identity-token");

		Assert.True(attestation.Succeeded);
		Assert.Equal("signed.member.statement", attestation.Token);
		Assert.Equal("POST", handler.Method);
		Assert.Equal("https://aa-bristol.org/amber/wp-json/fellowship/v1/attest/member", handler.Url);
		Assert.Equal("Bearer identity-token", handler.Authorization);
	}

	[Fact]
	public async Task OnlyTheAudienceIsSentToBeVouchedFor()
	{
		var handler = new Stub(HttpStatusCode.OK, """{"attestation":"signed.audience.statement"}""");

		var attestation = await Client(handler).AttestAudienceAsync("device-token", new SendRequest
		{
			Subject = "Secret subject",
			Body = "Secret body",
			MemberIds = [8, 9],
			Committees = ["steering"],
			AllGsrs = true,
		});

		Assert.Equal("signed.audience.statement", attestation.Token);
		Assert.Equal("https://aa-bristol.org/amber/wp-json/fellowship/v1/attest/audience", handler.Url);
		Assert.Equal("Bearer device-token", handler.Authorization);

		var body = System.Text.Json.JsonDocument.Parse(handler.Body).RootElement;
		Assert.Equal(8, body.GetProperty("memberIds")[0].GetInt64());
		Assert.Equal(9, body.GetProperty("memberIds")[1].GetInt64());
		Assert.Equal("steering", body.GetProperty("committees")[0].GetString());
		Assert.True(body.GetProperty("gsrs").GetBoolean());
		Assert.DoesNotContain("Secret", handler.Body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnAudienceLeavesOutWhatItDoesNotAddress()
	{
		var handler = new Stub(HttpStatusCode.OK, """{"attestation":"a"}""");

		await Client(handler).AttestAudienceAsync("t", new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.Equal("""{"memberIds":[8]}""", handler.Body);
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, """{"code":"fellowship_not_a_member","message":"That address does not match a member record.","data":{"status":403}}""", "That address does not match a member record.")]
	[InlineData(HttpStatusCode.Forbidden, "<html>blocked</html>", "Your membership could not be confirmed. Please try again.")]
	[InlineData(HttpStatusCode.OK, "{}", "Your membership could not be confirmed. Please try again.")]
	[InlineData(HttpStatusCode.OK, "not json", "Your membership could not be confirmed. Please try again.")]
	public async Task AMemberTheSiteWillNotVouchForGetsTheSitesWords(HttpStatusCode status, string body, string expected)
	{
		var attestation = await Client(new Stub(status, body)).AttestMemberAsync("t");

		Assert.False(attestation.Succeeded);
		Assert.True(attestation.Arrived);
		Assert.Equal(expected, attestation.Error);
	}

	[Theory]
	[InlineData("""{"code":"fellowship_committee_send_disabled","message":"That committee does not take messages from Link.","data":{"status":403}}""", "That committee does not take messages from Link.")]
	[InlineData("""{"code":"fellowship_no_recipients"}""", "The message could not be sent.")]
	public async Task AnAudienceTheSiteWillNotVouchForGetsTheSitesWords(string body, string expected)
	{
		var attestation = await Client(new Stub(HttpStatusCode.Forbidden, body))
			.AttestAudienceAsync("t", new SendRequest { Subject = "s", Body = "b", Committees = ["steering"] });

		Assert.False(attestation.Succeeded);
		Assert.Equal(expected, attestation.Error);
	}

	[Fact]
	public async Task NoNetworkWhileAskingToBeVouchedForSaysSo()
	{
		var attestation = await Client(new Stub(new HttpRequestException("offline"))).AttestMemberAsync("t");

		Assert.False(attestation.Succeeded);
		Assert.False(attestation.Arrived);
		Assert.Contains("Could not reach the intergroup", attestation.Error, StringComparison.Ordinal);
	}

	private static SiteClient Client(HttpMessageHandler handler) => new(new HttpClient(handler), Config);

	private sealed class Stub : HttpMessageHandler
	{
		private readonly HttpStatusCode _status;
		private readonly string _body;
		private readonly Exception? _throws;

		public Stub(HttpStatusCode status, string body)
		{
			_status = status;
			_body = body;
		}

		public Stub(Exception throws)
		{
			_throws = throws;
			_body = string.Empty;
		}

		public string Method { get; private set; } = string.Empty;

		public string Url { get; private set; } = string.Empty;

		public string Authorization { get; private set; } = string.Empty;

		public string Body { get; private set; } = string.Empty;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (_throws is not null)
			{
				throw _throws;
			}

			Method = request.Method.Method;
			Url = request.RequestUri!.ToString();
			Authorization = request.Headers.Authorization?.ToString() ?? string.Empty;

			if (request.Content is not null)
			{
				Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			}

			return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
		}
	}
}
