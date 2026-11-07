using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Link's Firebase client against a stand-in for Google.
///
/// <para>These pin what the phone sends and how it reads the answers. They can't
/// show that Google agrees: the Functions' and rules' own tests cover the other
/// side, and the two meet for real on a phone at step 8.</para>
/// </summary>
public sealed class FirebaseFellowshipClientTests
{
	private const string RefreshToken = "refresh-token";
	private const string DeviceId = "d_abc";
	private const long MemberId = 7;
	private const long MessageId = 1_835_000_000_000L * 1024;

	private static readonly string IdToken = Jwt(DeviceId, MemberId);

	private readonly FakeGoogle _google = new();
	private readonly SiteStub _site = new();

	[Fact]
	public async Task EnrollingWithAGoogleTokenEndsWithARefreshTokenInTheSession()
	{
		_google.Enrols();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };

		var result = await Client().EnrolAsync(new EnrolmentRequest
		{
			FirebaseToken = "google-identity",
			PublicKey = "spki",
			Platform = "android",
			Label = "Pixel",
		});

		Assert.True(result.Succeeded);
		Assert.Equal(RefreshToken, result.Session!.Token);
		Assert.Equal(DeviceId, result.Session.FirebaseDeviceId);
		Assert.Equal(MemberId, result.Session.MemberId);
		Assert.Equal("Dave B", result.Session.MemberName);

		var enrol = _google.Only("/enrol");
		Assert.Equal("Bearer google-identity", enrol.Authorization);

		var data = JsonDocument.Parse(enrol.Body).RootElement.GetProperty("data");
		Assert.Equal("spki", data.GetProperty("publicKey").GetString());
		Assert.Equal("android", data.GetProperty("platform").GetString());
		Assert.Equal("Pixel", data.GetProperty("label").GetString());
		Assert.False(data.TryGetProperty("pushToken", out _));

		// The directory is asked as the new device, because the identity just used
		// no longer exists.
		Assert.Equal(IdToken, _site.DirectoryAskedWith);
	}

	[Fact]
	public async Task TakingOverAskedForGivesThisPhoneThatDeviceAndEnrolsNothing()
	{
		_google.TakesOver();

		var result = await Client().EnrolAsync(TakingOver());

		Assert.True(result.TookOver);
		Assert.Equal(DeviceId, result.Session!.FirebaseDeviceId);
		Assert.Equal(MemberId, result.Session.MemberId);
		Assert.Equal(RefreshToken, result.Session.Token);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("/enrol", StringComparison.Ordinal));

		var taking = _google.Only("/takeOverDevice");
		Assert.Equal("Bearer google-identity", taking.Authorization);

		var data = JsonDocument.Parse(taking.Body).RootElement.GetProperty("data");
		Assert.Equal(DeviceId, data.GetProperty("deviceId").GetString());
		Assert.Equal("backup-spki", data.GetProperty("publicKey").GetString());
		Assert.Equal("ios", data.GetProperty("platform").GetString());
		Assert.Equal("New phone", data.GetProperty("label").GetString());
		Assert.Equal("push", data.GetProperty("pushToken").GetString());
	}

	[Fact]
	public async Task AnOrdinarySignInTakesNothingOver()
	{
		_google.Enrols();

		var result = await Client().EnrolAsync(new EnrolmentRequest { FirebaseToken = "google-identity", PublicKey = "spki", Platform = "android" });

		Assert.False(result.TookOver);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("takeOverDevice", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("fellowship_key_mismatch")]
	[InlineData("fellowship_no_such_device")]
	[InlineData("fellowship_device_revoked")]
	[InlineData("fellowship_bad_public_key")]
	public async Task ADeviceThatCanNotBeTakenOverEnrolsAsNewWithTheSameSignIn(string reason)
	{
		_google.Enrols();
		_google.Routes["/takeOverDevice"] = _ => Refusal(reason, "No.");

		var result = await Client().EnrolAsync(TakingOver());

		Assert.True(result.Succeeded);
		Assert.False(result.TookOver);

		var enrol = _google.Only("/enrol");
		Assert.Equal("Bearer google-identity", enrol.Authorization);

		// Its own new key, not the backup's.
		Assert.Equal("new-spki", JsonDocument.Parse(enrol.Body).RootElement.GetProperty("data").GetProperty("publicKey").GetString());
	}

	[Fact]
	public async Task AppleIsAskedOnceWhenATakeoverIsRefused()
	{
		// Firebase refuses an Apple credential presented twice.
		_google.Enrols();
		_google.Routes["/takeOverDevice"] = _ => Refusal("fellowship_key_mismatch", "No.");
		_google.Routes["signInWithIdp"] = _ => FakeGoogle.Json("""{"idToken":"apple-identity","refreshToken":"r","expiresIn":"3600"}""");

		var result = await Client().EnrolAsync(new EnrolmentRequest
		{
			IdToken = "apple-token",
			State = "state",
			PublicKey = "new-spki",
			Platform = "ios",
			TakeOverDeviceId = DeviceId,
			TakeOverPublicKey = "backup-spki",
		});

		Assert.True(result.Succeeded);
		_google.Only("signInWithIdp");
	}

	[Fact]
	public async Task NotBeingAMemberStopsTheSignInRatherThanEnrolling()
	{
		_google.Enrols();
		_google.Routes["/takeOverDevice"] = _ => Refusal("fellowship_not_a_member", "That address does not match a member record.");

		var result = await Client().EnrolAsync(TakingOver());

		Assert.False(result.Succeeded);
		Assert.Equal("That address does not match a member record.", result.Error);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("/enrol", StringComparison.Ordinal));
	}

	[Fact]
	public async Task NoAnswerToATakeoverStopsTheSignInRatherThanEnrolling()
	{
		_google.Enrols();
		_google.Routes["/takeOverDevice"] = _ => throw new HttpRequestException("offline");

		var result = await Client().EnrolAsync(TakingOver());

		Assert.False(result.Succeeded);
		Assert.NotEmpty(result.Error);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("/enrol", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AKeyThatCouldNotBeReadSkipsTheTakeover()
	{
		_google.Enrols();

		var result = await Client().EnrolAsync(TakingOver() with { TakeOverPublicKey = string.Empty });

		Assert.True(result.Succeeded);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("takeOverDevice", StringComparison.Ordinal));
	}

	private static EnrolmentRequest TakingOver() => new()
	{
		FirebaseToken = "google-identity",
		PublicKey = "new-spki",
		Platform = "ios",
		Label = "New phone",
		PushToken = "push",
		TakeOverDeviceId = DeviceId,
		TakeOverPublicKey = "backup-spki",
	};

	[Fact]
	public async Task AnEnrolmentStillWorksWhenWordPressCanNotSayTheName()
	{
		_google.Enrols();

		var result = await Client().EnrolAsync(new EnrolmentRequest { FirebaseToken = "x", PublicKey = "spki", Platform = "ios" });

		Assert.True(result.Succeeded);
		Assert.Equal(string.Empty, result.Session!.MemberName);
	}

	[Fact]
	public async Task APasswordGoesOnlyToFellowship()
	{
		_google.Enrols();
		_google.Routes["/auth/identity/password"] = _ => FakeGoogle.Json("""{"token":"pw-custom"}""");
		_google.Routes["signInWithCustomToken"] = request => request.Body.Contains("pw-custom", StringComparison.Ordinal)
			? FakeGoogle.Json($$$$$$"""{"idToken":"pw-identity","refreshToken":"pw-refresh","expiresIn":"3600"}""")
			: FakeGoogle.Json($$$$$$"""{"idToken":"{{{{{{IdToken}}}}}}","refreshToken":"{{{{{{RefreshToken}}}}}}","expiresIn":"3600"}""");

		var result = await Client().EnrolAsync(new EnrolmentRequest
		{
			Email = "dave@example.org",
			Password = "correct horse battery",
			PublicKey = "spki",
			Platform = "android",
		});

		Assert.True(result.Succeeded);
		Assert.Equal("Bearer pw-identity", _google.Only("/enrol").Authorization);

		var carrying = _google.Requests.Where(r => r.Body.Contains("correct horse", StringComparison.Ordinal)).ToList();
		Assert.Single(carrying);
		Assert.StartsWith("https://aa-bristol.org/wp-json/fellowship/v1/auth/identity/password", carrying[0].Url, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ARefusedPasswordSaysWhatFellowshipSaid()
	{
		_google.Routes["/auth/identity/password"] = _ => new FakeReply(
			HttpStatusCode.Unauthorized,
			"""{"code":"fellowship_bad_credentials","message":"That address and password do not match."}""");

		var result = await Client().EnrolAsync(new EnrolmentRequest { Email = "a@b.c", Password = "nope", PublicKey = "spki", Platform = "android" });

		Assert.Equal("That address and password do not match.", result.Error);
		Assert.DoesNotContain(_google.Requests, r => r.Url.Contains("/enrol", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AppleIsGivenTheHashAndFirebaseTheRawNonce()
	{
		var start = await Client().StartSignInAsync("apple");

		Assert.NotNull(start);
		Assert.Equal(
			Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(start.State))),
			start.Nonce);

		_google.Enrols();
		_google.Routes["signInWithIdp"] = _ => FakeGoogle.Json("""{"idToken":"apple-identity","refreshToken":"r","expiresIn":"3600"}""");

		await Client().EnrolAsync(new EnrolmentRequest { IdToken = "apple-token", State = start.State, PublicKey = "spki", Platform = "ios" });

		var postBody = JsonDocument.Parse(_google.Only("signInWithIdp").Body).RootElement.GetProperty("postBody").GetString();
		Assert.Contains("id_token=apple-token", postBody, StringComparison.Ordinal);
		Assert.Contains("providerId=apple.com", postBody, StringComparison.Ordinal);
		Assert.Contains("nonce=" + start.State, postBody, StringComparison.Ordinal);
		Assert.Equal("Bearer apple-identity", _google.Only("/enrol").Authorization);
	}

	[Fact]
	public async Task GoogleHasNoBrowserFlowHere() =>
		Assert.Null(await Client().StartSignInAsync("google"));

	[Fact]
	public async Task NoCredentialAtAllAsksNothingOfAnybody()
	{
		var result = await Client().EnrolAsync(new EnrolmentRequest { PublicKey = "spki", Platform = "android" });

		Assert.False(result.Succeeded);
		Assert.Empty(_google.Requests);
	}

	[Fact]
	public async Task ABuildWithNoFirebaseConfigurationSaysSoRatherThanFailingOddly()
	{
		var client = new FirebaseFellowshipClient(
			new HttpClient(_google),
			new FirebaseConfiguration(),
			new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org" },
			_site);

		var result = await client.EnrolAsync(new EnrolmentRequest { FirebaseToken = "x", PublicKey = "spki", Platform = "android" });

		Assert.Contains("no Firebase configuration", result.Error, StringComparison.Ordinal);
		Assert.Empty(_google.Requests);
	}

	[Fact]
	public void TheApiKeyIsReadWhenFirstNeededAndOnlyOnce()
	{
		var reads = 0;
		var configuration = new FirebaseConfiguration
		{
			ApiKeySource = () =>
			{
				reads++;
				return "from-the-platform";
			},
		};

		Assert.Equal(0, reads);
		Assert.Equal("from-the-platform", configuration.ApiKey);
		Assert.True(configuration.IsConfigured);
		Assert.Equal(1, reads);
	}

	[Fact]
	public async Task FreedomIsGivenTheIdTokenAndNeverTheRefreshToken()
	{
		_google.SignedIn();

		Assert.Equal(IdToken, await Client().SessionProofAsync(RefreshToken));
	}

	[Fact]
	public async Task NoProofIsEmptyRatherThanAnException()
	{
		_google.Throws = new HttpRequestException("offline");

		Assert.Equal(string.Empty, await Client().SessionProofAsync(RefreshToken));
	}

	[Fact]
	public async Task ARefusedEnrolmentSaysWhatTheFunctionSaid()
	{
		_google.Routes["/enrol"] = _ => new FakeReply(
			HttpStatusCode.Forbidden,
			"""{"error":{"status":"PERMISSION_DENIED","message":"That address does not match a member record.","details":{"reason":"fellowship_not_a_member"}}}""");

		var result = await Client().EnrolAsync(new EnrolmentRequest { FirebaseToken = "x", PublicKey = "spki", Platform = "android" });

		Assert.Equal("That address does not match a member record.", result.Error);
	}

	[Fact]
	public async Task TheIdTokenIsFetchedOnceAndHeld()
	{
		_google.SignedIn();
		_google.InboxAnswers("""[{"readTime":"2026-11-06T00:00:00Z"}]""");

		var client = Client();
		await client.FetchInboxAsync(RefreshToken, 0);
		await client.FetchInboxAsync(RefreshToken, 0);

		Assert.Equal(2, _google.Requests.Count(r => r.Url.Contains(":runQuery", StringComparison.Ordinal)));

		var refresh = _google.Only("securetoken");
		Assert.Contains("grant_type=refresh_token", refresh.Body, StringComparison.Ordinal);
		Assert.Contains("refresh_token=" + RefreshToken, refresh.Body, StringComparison.Ordinal);
		Assert.All(
			_google.Requests.Where(r => r.Url.Contains(":runQuery", StringComparison.Ordinal)),
			r => Assert.Equal("Bearer " + IdToken, r.Authorization));
	}

	[Fact]
	public async Task ATokenAboutToExpireIsFetchedAgain()
	{
		_google.SignedIn(expiresIn: 60);
		_google.InboxAnswers("[]");

		var client = Client();
		await client.FetchInboxAsync(RefreshToken, 0);
		await client.FetchInboxAsync(RefreshToken, 0);

		Assert.Equal(2, _google.Requests.Count(r => r.Url.Contains("securetoken", StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"TOKEN_EXPIRED"}}""", FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"USER_DISABLED"}}""", FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"USER_NOT_FOUND"}}""", FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"INVALID_REFRESH_TOKEN"}}""", FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"QUOTA_EXCEEDED"}}""", FellowshipFailure.Server)]
	[InlineData(HttpStatusCode.ServiceUnavailable, "<html>", FellowshipFailure.Server)]
	public async Task OnlyAFinishedTokenSignsThePhoneOut(HttpStatusCode status, string body, FellowshipFailure expected)
	{
		_google.Routes["securetoken"] = _ => new FakeReply(status, body);

		var page = await Client().FetchInboxAsync(RefreshToken, 0);

		Assert.False(page.Succeeded);
		Assert.Equal(expected, page.Failure);
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.Unauthorized, FellowshipFailure.Unauthenticated)]
	[InlineData(HttpStatusCode.ServiceUnavailable, FellowshipFailure.Server)]
	public async Task WhatTheRulesAnswerDecidesWhatAFailedFetchMeans(HttpStatusCode status, FellowshipFailure expected)
	{
		_google.SignedIn();
		_google.Routes[":runQuery"] = _ => new FakeReply(status, """{"error":{"message":"Missing or insufficient permissions."}}""");

		var page = await Client().FetchInboxAsync(RefreshToken, 0);

		Assert.False(page.Succeeded);
		Assert.Equal(expected, page.Failure);
	}

	[Fact]
	public async Task NoNetworkIsNeitherARefusalNorAnException()
	{
		_google.Throws = new HttpRequestException("offline");

		var page = await Client().FetchInboxAsync(RefreshToken, 0);

		Assert.False(page.Succeeded);
		Assert.Equal(FellowshipFailure.Network, page.Failure);
	}

	[Fact]
	public async Task TheInboxLooksBehindItsMarkForLateArrivals()
	{
		_google.SignedIn();
		_google.InboxAnswers("[]");

		await Client().FetchInboxAsync(RefreshToken, MessageId);

		var query = _google.Only($"/devices/{DeviceId}:runQuery");
		var structured = JsonDocument.Parse(query.Body).RootElement.GetProperty("structuredQuery");
		var filter = structured.GetProperty("where").GetProperty("fieldFilter");

		Assert.Equal("inbox", structured.GetProperty("from")[0].GetProperty("collectionId").GetString());
		Assert.Equal("seq", filter.GetProperty("field").GetProperty("fieldPath").GetString());
		Assert.Equal("GREATER_THAN", filter.GetProperty("op").GetString());
		Assert.Equal(
			(MessageId - FirebaseFellowshipClient.LateArrivalOverlap).ToString(System.Globalization.CultureInfo.InvariantCulture),
			filter.GetProperty("value").GetProperty("integerValue").GetString());
	}

	[Fact]
	public void TheLookBehindCoversAnIdTheServerWouldStillAccept()
	{
		// Functions' ID_WINDOW_MILLISECONDS is ten minutes.
		Assert.True(FirebaseFellowshipClient.LateArrivalOverlap >= 10 * 60 * 1000 * 1024L);
	}

	[Fact]
	public async Task InboxRowsBecomeEnvelopes()
	{
		_google.SignedIn();
		_google.InboxAnswers($$$$$$"""
			[
				{"document":{"name":"x/inbox/{{{{{{MessageId}}}}}}","fields":{"k":{"stringValue":"key"},"p":{"stringValue":"sealed"},"seq":{"integerValue":"{{{{{{MessageId}}}}}}"},"senderMemberId":{"integerValue":"8"}}}},
				{"document":{"name":"x/inbox/2","fields":{"seq":{"integerValue":"2"}}}}
			]
			""");

		var page = await Client().FetchInboxAsync(RefreshToken, 0);

		var envelope = Assert.Single(page.Messages);
		Assert.Equal(MessageId, envelope.Id);
		Assert.Equal("key", envelope.WrappedKey);
		Assert.Equal("sealed", envelope.Payload);
		Assert.Equal(8, envelope.SenderMemberId);
		Assert.False(page.More);
	}

	[Fact]
	public async Task AFullPageInsideTheLookBehindDoesNotStopThePoll()
	{
		_google.SignedIn();

		var overlapStart = MessageId - FirebaseFellowshipClient.LateArrivalOverlap;
		var pages = new Queue<string>([InboxRows(overlapStart + 1, FirebaseFellowshipClient.InboxPageSize), InboxRows(MessageId + 1, 3)]);
		_google.Routes[":runQuery"] = _ => FakeGoogle.Json(pages.Dequeue());

		var page = await Client().FetchInboxAsync(RefreshToken, MessageId);

		Assert.Equal(FirebaseFellowshipClient.InboxPageSize + 3, page.Messages.Count);
		Assert.False(page.More);
	}

	[Fact]
	public async Task AFullPagePastTheMarkSaysThereIsMore()
	{
		_google.SignedIn();
		_google.InboxAnswers(InboxRows(MessageId + 1, FirebaseFellowshipClient.InboxPageSize));

		var page = await Client().FetchInboxAsync(RefreshToken, MessageId);

		Assert.True(page.More);
	}

	[Fact]
	public async Task ReadingWritesOnlyThisMembersReceipt()
	{
		_google.SignedIn();
		_google.Routes["/receipts/"] = _ => FakeGoogle.Json("{}");

		Assert.True(await Client().MarkReadAsync(RefreshToken, MessageId));

		var write = _google.Only("/receipts/");
		Assert.Equal("PATCH", write.Method);
		Assert.Contains($"/messages/{MessageId}/receipts/{MemberId}?updateMask.fieldPaths=readAt", write.Url, StringComparison.Ordinal);

		var fields = JsonDocument.Parse(write.Body).RootElement.GetProperty("fields");
		Assert.True(fields.GetProperty("readAt").TryGetProperty("timestampValue", out _));
	}

	[Fact]
	public async Task ReceiptsTheRulesRefuseCountAsAccepted()
	{
		// A message already swept. Answering false would ask about it for ever.
		_google.SignedIn();
		_google.Routes["/receipts/"] = _ => new FakeReply(HttpStatusCode.Forbidden, "{}");

		Assert.True(await Client().MarkReceivedAsync(RefreshToken, [1, 2, 3]));
		Assert.Equal(3, _google.Requests.Count(r => r.Url.Contains("/receipts/", StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReceiptsThatDidNotArriveAreTriedAgain(bool outage)
	{
		_google.SignedIn();

		if (outage)
		{
			_google.Routes["/receipts/"] = _ => new FakeReply(HttpStatusCode.ServiceUnavailable, "{}");
		}
		else
		{
			_google.ThrowsFor = "/receipts/";
			_google.Throws = new HttpRequestException("offline");
		}

		Assert.False(await Client().MarkReceivedAsync(RefreshToken, [1, 2]));
	}

	[Fact]
	public async Task ReceiptsComeFromTheMessagesThisMemberSent()
	{
		_google.SignedIn();
		_google.Routes["documents:runQuery"] = _ => FakeGoogle.Json($$$$$$"""
			[
				{"document":{"name":"projects/p/databases/(default)/documents/messages/{{{{{{MessageId}}}}}}","fields":{"recipientCount":{"integerValue":"3"},"receivedCount":{"integerValue":"2"},"readCount":{"integerValue":"1"}}}},
				{"document":{"name":"projects/p/databases/(default)/documents/messages/{{{{{{MessageId + 1}}}}}}","fields":{"recipientCount":{"integerValue":"9"}}}}
			]
			""");

		var receipts = await Client().FetchReceiptsAsync(RefreshToken, [MessageId, MessageId + 2]);

		Assert.Equal(new MessageReceipt(MessageId, 3, 2, 1), Assert.Single(receipts!));

		var filters = JsonDocument.Parse(_google.Only("documents:runQuery").Body).RootElement
			.GetProperty("structuredQuery").GetProperty("where").GetProperty("compositeFilter").GetProperty("filters");

		Assert.Equal("senderMemberId", filters[0].GetProperty("fieldFilter").GetProperty("field").GetProperty("fieldPath").GetString());
		Assert.Equal("7", filters[0].GetProperty("fieldFilter").GetProperty("value").GetProperty("integerValue").GetString());
		Assert.EndsWith($"/documents/messages/{MessageId}", filters[1].GetProperty("fieldFilter").GetProperty("value").GetProperty("referenceValue").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ReceiptsThatCouldNotBeAskedForAreNotAnEmptyAnswer()
	{
		_google.SignedIn();
		_google.Routes["documents:runQuery"] = _ => new FakeReply(HttpStatusCode.ServiceUnavailable, "{}");

		Assert.Null(await Client().FetchReceiptsAsync(RefreshToken, [MessageId]));
	}

	[Fact]
	public async Task MessagesSentThroughFellowshipAreLeftOutOfTheReceiptsQuery()
	{
		// As text 999 sorts after 1835…, so starting from it would miss every
		// message actually held here.
		_google.SignedIn();
		_google.Routes["documents:runQuery"] = _ => FakeGoogle.Json($$$$$$"""
			[{"document":{"name":"x/messages/{{{{{{MessageId}}}}}}","fields":{"recipientCount":{"integerValue":"1"}}}}]
			""");

		var receipts = await Client().FetchReceiptsAsync(RefreshToken, [999, MessageId]);

		Assert.Equal(MessageId, Assert.Single(receipts!).Id);

		var filters = JsonDocument.Parse(_google.Only("documents:runQuery").Body).RootElement
			.GetProperty("structuredQuery").GetProperty("where").GetProperty("compositeFilter").GetProperty("filters");

		Assert.EndsWith($"/documents/messages/{MessageId}", filters[1].GetProperty("fieldFilter").GetProperty("value").GetProperty("referenceValue").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task OnlyFellowshipsMessagesMeansNothingToAsk()
	{
		var receipts = await Client().FetchReceiptsAsync(RefreshToken, [999, 1000]);

		Assert.Empty(receipts!);
		Assert.Empty(_google.Requests);
	}

	[Fact]
	public async Task EachDeviceGetsItsOwnEnvelopeAndNoneOfItIsReadable()
	{
		using var sue = RSA.Create(Sealing.KeyBits);
		using var ann = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Resolves(MessageId, ("d_sue", sue), ("d_ann", ann));
		_google.Sends(MessageId);

		var result = await Client().SendAsync(RefreshToken, new SendRequest
		{
			Subject = "Secret subject",
			Body = "Secret body",
			MemberIds = [8, 9],
			Committees = ["steering"],
			AllGsrs = true,
			ReplyToId = 55,
		});

		Assert.True(result.Succeeded);
		Assert.Equal(MessageId, result.MessageId);
		Assert.Equal(2, result.Recipients);
		Assert.Equal(3, result.Addressed);

		Assert.DoesNotContain(_google.Requests, r => r.Body.Contains("Secret", StringComparison.Ordinal));

		var data = JsonDocument.Parse(_google.Only("/sendMessage").Body).RootElement.GetProperty("data");
		Assert.Equal(MessageId, data.GetProperty("id").GetInt64());
		Assert.Equal(8, data.GetProperty("memberIds")[0].GetInt64());
		Assert.Equal("steering", data.GetProperty("committees")[0].GetString());
		Assert.True(data.GetProperty("gsrs").GetBoolean());

		var envelopes = data.GetProperty("envelopes");
		Assert.Equal(2, envelopes.GetArrayLength());

		var opened = LinkMessage.FromPayload(MessagePayloadCipher.Open(
			envelopes[1].GetProperty("k").GetString()!,
			envelopes[1].GetProperty("p").GetString()!,
			ann.ExportPkcs8PrivateKeyPem())!);

		Assert.Equal("d_ann", envelopes[1].GetProperty("deviceId").GetString());
		Assert.NotNull(opened);
		Assert.Equal(MessageId, opened.Id);
		Assert.Equal("Secret subject", opened.Subject);
		Assert.Equal("Secret body", opened.Body);
		Assert.Equal("Dave B", opened.Sender);
		Assert.Equal(MemberId, opened.SenderId);
		Assert.Equal(55, opened.ReplyToId);
		Assert.Equal(1_835_000_000L, opened.CreatedAt);
		Assert.False(opened.IsRead);

		// Sealed to one device, so another's key doesn't open it.
		Assert.Null(MessagePayloadCipher.Open(
			envelopes[1].GetProperty("k").GetString()!,
			envelopes[1].GetProperty("p").GetString()!,
			sue.ExportPkcs8PrivateKeyPem()));
	}

	[Fact]
	public async Task AStaleAudienceIsResolvedAndSealedAgainOnce()
	{
		using var sue = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Resolves(MessageId, ("d_sue", sue));

		var answers = new Queue<FakeReply>([
			Refusal("fellowship_stale_audience", "Who this message reaches has changed."),
			FakeGoogle.Json($$$$$$"""{"result":{"id":{{{{{{MessageId}}}}}},"createdAt":1,"members":1,"reachable":1}}"""),
		]);
		_google.Routes["/sendMessage"] = _ => answers.Dequeue();

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.True(result.Succeeded);
		Assert.Equal(2, _google.Requests.Count(r => r.Url.EndsWith("/resolveAudience", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task AnAudienceStillStaleAfterTheRetryGivesUp()
	{
		using var sue = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Resolves(MessageId, ("d_sue", sue));
		_google.Routes["/sendMessage"] = _ => Refusal("fellowship_stale_audience", "Who this message reaches has changed.");

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.False(result.Succeeded);
		Assert.Equal("Who this message reaches has changed.", result.Error);
		Assert.Equal(2, _google.Requests.Count(r => r.Url.EndsWith("/sendMessage", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task OtherRefusedSendsAreNotRetried()
	{
		using var sue = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Resolves(MessageId, ("d_sue", sue));
		_google.Routes["/sendMessage"] = _ => Refusal("fellowship_rate_limited", "You have sent a lot of messages. Please wait.");

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.Equal("You have sent a lot of messages. Please wait.", result.Error);
		Assert.Single(_google.Requests, r => r.Url.EndsWith("/sendMessage", StringComparison.Ordinal));
	}

	[Fact]
	public async Task NobodyReachableIsSaidBeforeAnythingIsSealed()
	{
		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Routes["/resolveAudience"] = _ => FakeGoogle.Json($$$$$$"""{"result":{"id":{{{{{{MessageId}}}}}},"devices":[],"members":2,"reachable":0}}""");

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.Contains("have Link", result.Error, StringComparison.Ordinal);
		Assert.DoesNotContain(_google.Requests, r => r.Url.EndsWith("/sendMessage", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AMessageTooLongForAPushIsRefusedWithWordsToActOn()
	{
		using var sue = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = MemberId, Name = "Dave B" }] };
		_google.Resolves(MessageId, ("d_sue", sue));

		// Random, so that gzip can't shrink it.
		var body = Convert.ToBase64String(RandomNumberGenerator.GetBytes(6000));

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = body, MemberIds = [8] });

		Assert.Contains("too long", result.Error, StringComparison.Ordinal);
		Assert.DoesNotContain(_google.Requests, r => r.Url.EndsWith("/sendMessage", StringComparison.Ordinal));
	}

	[Fact]
	public async Task SendingNeedsWordPressToSayWhoTheSenderIs()
	{
		_google.SignedIn();

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.Contains("not been sent", result.Error, StringComparison.Ordinal);
		Assert.DoesNotContain(_google.Requests, r => r.Url.EndsWith("/resolveAudience", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AMemberTheDirectoryDoesNotListSealsNoName()
	{
		using var sue = RSA.Create(Sealing.KeyBits);

		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = 99, Name = "Somebody else" }] };
		_google.Resolves(MessageId, ("d_sue", sue));
		_google.Sends(MessageId);

		var result = await Client().SendAsync(RefreshToken, new SendRequest { Subject = "s", Body = "b", MemberIds = [8] });

		Assert.True(result.Succeeded);

		var envelope = JsonDocument.Parse(_google.Only("/sendMessage").Body).RootElement.GetProperty("data").GetProperty("envelopes")[0];
		var opened = LinkMessage.FromPayload(MessagePayloadCipher.Open(
			envelope.GetProperty("k").GetString()!,
			envelope.GetProperty("p").GetString()!,
			sue.ExportPkcs8PrivateKeyPem())!);

		Assert.Equal(string.Empty, opened!.Sender);
	}

	[Fact]
	public async Task ThePushTokenAndAKeyFaultAreWrittenToThisDeviceOnly()
	{
		_google.SignedIn();
		_google.Routes[$"/devices/{DeviceId}?"] = _ => FakeGoogle.Json("{}");

		var client = Client();
		Assert.True(await client.UpdatePushTokenAsync(RefreshToken, "fcm-token"));
		Assert.True(await client.ReportKeyFaultAsync(RefreshToken));

		var writes = _google.Requests.Where(r => r.Method == "PATCH").ToList();
		Assert.EndsWith("updateMask.fieldPaths=pushToken", writes[0].Url, StringComparison.Ordinal);
		Assert.Equal("fcm-token", JsonDocument.Parse(writes[0].Body).RootElement.GetProperty("fields").GetProperty("pushToken").GetProperty("stringValue").GetString());
		Assert.EndsWith("updateMask.fieldPaths=keyFault", writes[1].Url, StringComparison.Ordinal);
		Assert.True(JsonDocument.Parse(writes[1].Body).RootElement.GetProperty("fields").GetProperty("keyFault").GetProperty("booleanValue").GetBoolean());
	}

	[Fact]
	public async Task ReplacingAKeyProvesWhoIsHoldingThePhone()
	{
		_google.SignedIn();
		_google.Routes["/replaceKey"] = _ => FakeGoogle.Json($$$$$$"""{"result":{"deviceId":"{{{{{{DeviceId}}}}}}"}}""");

		var result = await Client().RotateKeyAsync(RefreshToken, new RotateKeyRequest { FirebaseToken = "google-identity", PublicKey = "new-key" });

		Assert.True(result.Succeeded);

		var call = _google.Only("/replaceKey");
		Assert.Equal("Bearer google-identity", call.Authorization);

		var data = JsonDocument.Parse(call.Body).RootElement.GetProperty("data");
		Assert.Equal(DeviceId, data.GetProperty("deviceId").GetString());
		Assert.Equal("new-key", data.GetProperty("publicKey").GetString());
	}

	[Fact]
	public async Task ARefusedKeyReplacementSaysWhy()
	{
		_google.SignedIn();
		_google.Routes["/replaceKey"] = _ => Refusal("fellowship_not_a_member", "Sign in as the member this phone belongs to.");

		var result = await Client().RotateKeyAsync(RefreshToken, new RotateKeyRequest { FirebaseToken = "x", PublicKey = "new-key" });

		Assert.Equal("Sign in as the member this phone belongs to.", result.Error);
	}

	[Fact]
	public async Task SigningOutForgetsTheToken()
	{
		_google.SignedIn();
		_google.Routes["/signOut"] = _ => FakeGoogle.Json("""{"result":{}}""");
		_google.InboxAnswers("[]");

		var client = Client();

		Assert.True(await client.SignOutAsync(RefreshToken));
		Assert.Equal("Bearer " + IdToken, _google.Only("/signOut").Authorization);

		await client.FetchInboxAsync(RefreshToken, 0);

		Assert.Equal(2, _google.Requests.Count(r => r.Url.Contains("securetoken", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task TheDirectoryIsWordPressesAskedAsThisDevice()
	{
		_google.SignedIn();
		_site.Directory = new FellowshipDirectory { Members = [new DirectoryMember { Id = 8, Name = "Sue" }] };

		var directory = await Client().FetchDirectoryAsync(RefreshToken);

		Assert.Equal("Sue", Assert.Single(directory.Members).Name);
		Assert.Equal(IdToken, _site.DirectoryAskedWith);
	}

	[Fact]
	public void ASealedMessageOpensTheWayFellowshipsDo()
	{
		using var rsa = RSA.Create(Sealing.KeyBits);

		var envelope = MessageSealer.Seal(Sealing.Payload(), Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));
		var opened = MessagePayloadCipher.Open(envelope.WrappedKey, envelope.Payload, rsa.ExportPkcs8PrivateKeyPem());

		Assert.NotNull(opened);
		Assert.Equal(Sealing.ExpectedSubject, opened["subject"].GetString());
		Assert.Equal(Sealing.ExpectedId, opened["id"].GetInt64());
	}

	[Fact]
	public void SealingTwiceSharesNothing()
	{
		using var rsa = RSA.Create(Sealing.KeyBits);
		var key = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

		var first = MessageSealer.Seal(Sealing.Payload(), key);
		var second = MessageSealer.Seal(Sealing.Payload(), key);

		Assert.NotEqual(first.WrappedKey, second.WrappedKey);
		Assert.NotEqual(first.Payload, second.Payload);
	}

	[Fact]
	public void AKeyThatIsNotOneIsRefused()
	{
		Assert.Throws<FormatException>(() => MessageSealer.Seal(Sealing.Payload(), "not base64!"));
		Assert.ThrowsAny<CryptographicException>(() => MessageSealer.Seal(Sealing.Payload(), Convert.ToBase64String([1, 2, 3])));
	}

	private FirebaseFellowshipClient Client() => new(
		new HttpClient(_google),
		new FirebaseConfiguration { ApiKey = "api-key" },
		new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org" },
		_site);

	private static FakeReply Refusal(string reason, string message) => new(
		HttpStatusCode.BadRequest,
		JsonSerializer.Serialize(new { error = new { status = "FAILED_PRECONDITION", message, details = new { reason } } }));

	private static string InboxRows(long first, int count) =>
		"[" + string.Join(',', Enumerable.Range(0, count).Select(i =>
			$$$$$$"""{"document":{"fields":{"k":{"stringValue":"k"},"p":{"stringValue":"p"},"seq":{"integerValue":"{{{{{{first + i}}}}}}"},"senderMemberId":{"integerValue":"8"}}}}""")) + "]";

	private static string Jwt(string deviceId, long memberId)
	{
		var claims = JsonSerializer.SerializeToUtf8Bytes(new { sub = deviceId, memberId, kind = "device" });

		return "e30." + System.Buffers.Text.Base64Url.EncodeToString(claims) + ".signature";
	}

	private sealed record FakeRequest(string Method, string Url, string Authorization, string Body);

	private sealed record FakeReply(HttpStatusCode Status, string Body);

	/// <summary>Answers by the first route whose text is in the URL. An unrouted request is a 404.</summary>
	private sealed class FakeGoogle : HttpMessageHandler
	{
		private readonly Lock _gate = new();

		public Dictionary<string, Func<FakeRequest, FakeReply>> Routes { get; } = [];

		public List<FakeRequest> Requests { get; } = [];

		public Exception? Throws { get; set; }

		public string ThrowsFor { get; set; } = string.Empty;

		public static FakeReply Json(string body) => new(HttpStatusCode.OK, body);

		public FakeRequest Only(string urlPart) =>
			Assert.Single(Requests, r => r.Url.Contains(urlPart, StringComparison.Ordinal));

		public void SignedIn(int expiresIn = 3600) =>
			Routes["securetoken"] = _ => Json($$$$$$"""{"id_token":"{{{{{{IdToken}}}}}}","refresh_token":"{{{{{{RefreshToken}}}}}}","expires_in":"{{{{{{expiresIn}}}}}}"}""");

		public void InboxAnswers(string rows) => Routes[":runQuery"] = _ => Json(rows);

		public void TakesOver()
		{
			Enrols();
			Routes["/takeOverDevice"] = Routes["/enrol"];
		}

		public void Enrols()
		{
			Routes["/enrol"] = _ => Json($$$$$$"""{"result":{"deviceId":"{{{{{{DeviceId}}}}}}","memberId":{{{{{{MemberId}}}}}},"token":"device-custom"}}""");
			Routes["signInWithCustomToken"] = _ => Json($$$$$$"""{"idToken":"{{{{{{IdToken}}}}}}","refreshToken":"{{{{{{RefreshToken}}}}}}","expiresIn":"3600"}""");
		}

		public void Resolves(long id, params (string DeviceId, RSA Key)[] devices)
		{
			var listed = devices.Select(d => new
			{
				deviceId = d.DeviceId,
				memberId = 8,
				publicKey = Convert.ToBase64String(d.Key.ExportSubjectPublicKeyInfo()),
			});

			var answer = JsonSerializer.Serialize(new { result = new { id, devices = listed, members = devices.Length + 1, reachable = devices.Length } });

			Routes["/resolveAudience"] = _ => Json(answer);
		}

		public void Sends(long id) =>
			Routes["/sendMessage"] = _ => Json($$$$$$"""{"result":{"id":{{{{{{id}}}}}},"createdAt":1835000000,"members":3,"reachable":2}}""");

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = request.Content is null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			var recorded = new FakeRequest(
				request.Method.Method,
				request.RequestUri!.ToString(),
				request.Headers.Authorization?.ToString() ?? string.Empty,
				body);

			lock (_gate)
			{
				Requests.Add(recorded);
			}

			if (Throws is not null && recorded.Url.Contains(ThrowsFor, StringComparison.Ordinal))
			{
				throw Throws;
			}

			foreach (var (part, answer) in Routes)
			{
				if (recorded.Url.Contains(part, StringComparison.Ordinal))
				{
					var reply = answer(recorded);

					return new HttpResponseMessage(reply.Status)
					{
						Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
					};
				}
			}

			return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
		}
	}

	/// <summary>What stays in WordPress.</summary>
	private sealed class SiteStub : ISiteClient
	{
		public FellowshipDirectory Directory { get; set; } = FellowshipDirectory.Empty;

		public string DirectoryAskedWith { get; private set; } = string.Empty;

		public Task<FellowshipDirectory> FetchDirectoryAsync(string idToken, CancellationToken cancellationToken = default)
		{
			DirectoryAskedWith = idToken;

			return Task.FromResult(Directory);
		}

		public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(true);

		public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) =>
			Task.FromResult(PasswordSetResult.Ok());
	}
}
