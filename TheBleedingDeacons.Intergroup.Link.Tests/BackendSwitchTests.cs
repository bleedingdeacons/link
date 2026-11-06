using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Which back end a phone uses, and what moving does to it.
///
/// <para>The history here is the real one, on disk, because what matters about a
/// move is what the next poll asks for.</para>
/// </summary>
public sealed class BackendSwitchTests : IDisposable
{
	private const long FirebaseId = 1_837_000_000_000_000L;

	private static readonly DeviceSession FellowshipSession = new() { Token = "fdt_test", MemberId = 7 };

	private static readonly DeviceSession FirebaseSession = new() { Token = "refresh", MemberId = 7, FirebaseDeviceId = "d_abc" };

	private readonly string _file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
	private readonly FakeSource _source = new();
	private readonly FakeStore _store = new();
	private readonly FakeSessions _sessions = new();
	private readonly FakeKeys _keys = new();
	private readonly JsonMessageHistory _history;
	private readonly object _listener = new();
	private readonly List<AuthenticationLost> _lost = [];
	private bool _firebaseReady = true;

	public BackendSwitchTests()
	{
		_history = new JsonMessageHistory(_file, JsonMessageHistory.KeyFrom(null, out _));
		WeakReferenceMessenger.Default.Register<AuthenticationLost>(_listener, (_, lost) => _lost.Add(lost));
	}

	public void Dispose()
	{
		WeakReferenceMessenger.Default.UnregisterAll(_listener);
		File.Delete(_file);
	}

	private BackendSwitch Switch() =>
		new(_source, _store, _sessions, _keys, () => _history, () => _firebaseReady);

	[Fact]
	public void APhoneStartsOnFellowship() =>
		Assert.Equal(LinkBackend.Fellowship, Switch().Current);

	[Fact]
	public async Task NoAnswerChangesNothing()
	{
		_store.Backend = LinkBackend.Firebase;
		_sessions.Session = FirebaseSession;
		_source.Answer = null;

		await Switch().RefreshAsync();

		Assert.Equal(LinkBackend.Firebase, _store.Backend);
		Assert.NotNull(_sessions.Session);
		Assert.Empty(_lost);
	}

	[Fact]
	public async Task ASignedOutPhoneJustFollows()
	{
		_source.Answer = LinkBackend.Firebase;

		await Switch().RefreshAsync();

		Assert.Equal(LinkBackend.Firebase, _store.Backend);
		Assert.Empty(_lost);
		Assert.Equal(0, _keys.Cleared);
	}

	[Fact]
	public async Task AMoveSignsThePhoneOutAndSaysWhy()
	{
		_sessions.Session = FellowshipSession;
		_source.Answer = LinkBackend.Firebase;
		await _history.SaveAsync([new LinkMessage { Id = 5, Subject = "Kept" }]);

		await Switch().RefreshAsync();

		Assert.Equal(LinkBackend.Firebase, _store.Backend);
		Assert.Null(_sessions.Session);
		Assert.Equal(1, _keys.Cleared);
		Assert.Contains("Sign in again", Assert.Single(_lost).Reason, StringComparison.Ordinal);
		Assert.Equal("Kept", Assert.Single(await _history.AllAsync()).Subject);
	}

	[Fact]
	public async Task TheSameAnswerTwiceDoesNothingTheSecondTime()
	{
		_sessions.Session = FirebaseSession;
		_store.Backend = LinkBackend.Firebase;
		_source.Answer = LinkBackend.Firebase;

		await Switch().RefreshAsync();

		Assert.NotNull(_sessions.Session);
		Assert.Empty(_lost);
	}

	[Fact]
	public async Task ASessionOnTheWrongBackEndIsCaughtEvenIfTheStoreAgrees()
	{
		// A crash part way through a move, or a restored preference. Its token must
		// not go to a back end that can't use it.
		_sessions.Session = FellowshipSession;
		_store.Backend = LinkBackend.Firebase;
		_source.Answer = LinkBackend.Firebase;

		await Switch().RefreshAsync();

		Assert.Null(_sessions.Session);
		Assert.Single(_lost);
	}

	[Fact]
	public async Task ABuildWithoutFirebaseStaysPut()
	{
		_firebaseReady = false;
		_sessions.Session = FellowshipSession;
		_source.Answer = LinkBackend.Firebase;

		await Switch().RefreshAsync();

		Assert.Equal(LinkBackend.Fellowship, _store.Backend);
		Assert.NotNull(_sessions.Session);
		Assert.Empty(_lost);
	}

	[Fact]
	public async Task MovingBackPutsThePollWhereFellowshipCanAnswerIt()
	{
		_store.Backend = LinkBackend.Firebase;
		_sessions.Session = FirebaseSession;
		_source.Answer = LinkBackend.Fellowship;
		await _history.SaveAsync([new LinkMessage { Id = 42 }, new LinkMessage { Id = FirebaseId }]);
		await _history.MarkPolledAsync(FirebaseId);

		await Switch().RefreshAsync();

		Assert.Equal(42, await _history.PollFromAsync());
		Assert.Equal(2, (await _history.AllAsync()).Count);
	}

	[Fact]
	public async Task MovingBackWithNothingFromFellowshipStartsFromTheBeginning()
	{
		_store.Backend = LinkBackend.Firebase;
		_source.Answer = LinkBackend.Fellowship;
		await _history.SaveAsync([new LinkMessage { Id = FirebaseId }]);
		await _history.MarkPolledAsync(FirebaseId);

		await Switch().RefreshAsync();

		Assert.Equal(0, await _history.PollFromAsync());
	}

	[Fact]
	public async Task AClearOnFirebaseDoesNotLeaveThePollStuckAfterMovingBack()
	{
		_store.Backend = LinkBackend.Firebase;
		_source.Answer = LinkBackend.Fellowship;
		await _history.SaveAsync([new LinkMessage { Id = FirebaseId }]);
		await _history.MarkPolledAsync(FirebaseId);
		await _history.ClearAsync();

		await Switch().RefreshAsync();

		Assert.True(await _history.PollFromAsync() < LinkMessageIds.FirstFirebaseId);
	}

	[Fact]
	public async Task MovingForwardLeavesThePollAlone()
	{
		_source.Answer = LinkBackend.Firebase;
		await _history.MarkPolledAsync(42);

		await Switch().RefreshAsync();

		Assert.Equal(42, await _history.PollFromAsync());
	}

	[Theory]
	[InlineData(HttpStatusCode.OK, """{"backend":"firebase"}""", LinkBackend.Firebase)]
	[InlineData(HttpStatusCode.OK, """{"backend":"fellowship"}""", LinkBackend.Fellowship)]
	[InlineData(HttpStatusCode.OK, """{"backend":"Firebase"}""", null)]
	[InlineData(HttpStatusCode.OK, """{"backend":"something"}""", null)]
	[InlineData(HttpStatusCode.OK, "{}", null)]
	[InlineData(HttpStatusCode.OK, "[]", null)]
	[InlineData(HttpStatusCode.Accepted, "<html>challenge</html>", null)]
	[InlineData(HttpStatusCode.NotFound, """{"code":"rest_no_route"}""", null)]
	[InlineData(HttpStatusCode.InternalServerError, "oops", null)]
	public async Task OnlyAClearAnswerFromTheSiteCounts(HttpStatusCode status, string body, LinkBackend? expected)
	{
		using var http = new HttpClient(new Stub(status, body));
		var client = new FellowshipClient(http, new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org/amber" });

		Assert.Equal(expected, await client.FetchBackendAsync());
	}

	[Fact]
	public async Task TheSwitchingClientCallsWhicheverBackEndIsCurrent()
	{
		var fellowship = new TrackingClient();
		var firebase = new TrackingClient();
		var built = 0;
		var client = new SwitchingFellowshipClient(Switch(), fellowship, () =>
		{
			built++;
			return firebase;
		});

		await client.FetchInboxAsync("token", 0);

		Assert.Equal(1, fellowship.Calls);
		Assert.Equal(0, built);

		_store.Backend = LinkBackend.Firebase;
		await client.FetchInboxAsync("token", 0);
		await client.FetchInboxAsync("token", 0);

		Assert.Equal(1, fellowship.Calls);
		Assert.Equal(2, firebase.Calls);
		Assert.Equal(1, built);
	}

	[Theory]
	[InlineData(3, 3, "")]
	[InlineData(5, 0, "")] // Fellowship doesn't report how many were addressed.
	[InlineData(2, 3, "Sent to 2 of 3 members. The other member doesn't have Link yet")]
	[InlineData(1, 3, "Sent to 1 of 3 members. The other 2 members don't have Link yet")]
	public void ASenderIsToldWhoItDidNotReach(int recipients, int addressed, string expected)
	{
		var shortfall = new SendResult { MessageId = 1, Recipients = recipients, Addressed = addressed }.Shortfall;

		if (expected.Length == 0)
		{
			Assert.Empty(shortfall);
		}
		else
		{
			Assert.StartsWith(expected, shortfall, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void ASessionKnowsWhichBackEndItBelongsTo()
	{
		Assert.Equal(LinkBackend.Fellowship, FellowshipSession.Backend);
		Assert.Equal(LinkBackend.Firebase, FirebaseSession.Backend);
	}

	private sealed class FakeSource : IBackendSource
	{
		public LinkBackend? Answer { get; set; }

		public Task<LinkBackend?> FetchBackendAsync(CancellationToken cancellationToken = default) => Task.FromResult(Answer);
	}

	private sealed class FakeStore : IBackendStore
	{
		public LinkBackend Backend { get; set; } = LinkBackend.Fellowship;
	}

	private sealed class FakeSessions : ISessionStore
	{
		public DeviceSession? Session { get; set; }

		public Task<DeviceSession?> LoadAsync() => Task.FromResult(Session);

		public Task SaveAsync(DeviceSession session)
		{
			Session = session;

			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			Session = null;

			return Task.CompletedTask;
		}
	}

	private sealed class FakeKeys : IDeviceKeyStore
	{
		public int Cleared { get; private set; }

		public Task<bool> HasKeyAsync() => Task.FromResult(false);

		public Task<string> PublicKeyAsync() => Task.FromResult(string.Empty);

		public Task<string> PrivateKeyAsync() => Task.FromResult(string.Empty);

		public Task<string> RegenerateAsync() => Task.FromResult(string.Empty);

		public Task ClearAsync()
		{
			Cleared++;

			return Task.CompletedTask;
		}
	}

	private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}

	private sealed class TrackingClient : IFellowshipClient
	{
		public int Calls { get; private set; }

		public Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default)
		{
			Calls++;

			return Task.FromResult(new InboxPage());
		}

		public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> MarkReceivedAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<IReadOnlyList<MessageReceipt>?> FetchReceiptsAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<RotateKeyResult> RotateKeyAsync(string token, RotateKeyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
