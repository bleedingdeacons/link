using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Inventory;
using TheBleedingDeacons.Inventory.BetterStack;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Where the Better Stack settings come from now that the app does not
/// carry them: Fellowship, once the handset has signed in.
///
/// <para>The distinction these keep is between "no answer" and "the
/// answer is do not ship". The first keeps whatever the handset has; the
/// second drops it. Muddling them would either stop a handset shipping
/// every time it lost signal, or keep one shipping after the intergroup
/// withdrew the token.</para>
/// </summary>
public sealed class RemoteLoggingTests
{
	private static readonly BetterStackConfiguration Shipping = new()
	{
		Endpoint = "https://s1.betterstackdata.com",
		SourceToken = "src-token",
	};

	private readonly FakeClient _client = new();
	private readonly FakeSessions _sessions = new();
	private readonly FakeStore _store = new();
	private readonly FakeController _controller = new();

	// ── Launch ────────────────────────────────────────────────────

	[Fact]
	public async Task ALaunchShipsWithWhatWasLastStored()
	{
		// Before the network has been tried: a push that wakes the
		// handset has no time to ask first.
		_store.Stored = Shipping;

		await Build().ApplyStoredAsync();

		Assert.Same(Shipping, Assert.Single(_controller.Applied));
	}

	[Fact]
	public async Task ALaunchWithNothingStoredHolds()
	{
		await Build().ApplyStoredAsync();

		Assert.Null(Assert.Single(_controller.Applied));
	}

	// ── Asking Fellowship ─────────────────────────────────────────

	[Fact]
	public async Task ANewAnswerIsStoredAndApplied()
	{
		_client.Answer = Shipping;

		await Build().RefreshAsync();

		Assert.Same(Shipping, _store.Stored);
		Assert.Same(Shipping, Assert.Single(_controller.Applied));
		Assert.Equal("fdt_test", _client.AskedWith);
	}

	[Fact]
	public async Task TheSameAnswerTwiceRebuildsNothing()
	{
		// Every launch asks, and a rebuild restarts the shipper. Nothing
		// has changed, so nothing should.
		_store.Stored = Shipping;
		_client.Answer = new BetterStackConfiguration { Endpoint = Shipping.Endpoint, SourceToken = Shipping.SourceToken };

		await Build().RefreshAsync();

		Assert.Empty(_controller.Applied);
	}

	[Fact]
	public async Task ANewTokenReplacesTheOld()
	{
		_store.Stored = Shipping;
		_client.Answer = new BetterStackConfiguration { Endpoint = Shipping.Endpoint, SourceToken = "rotated" };

		await Build().RefreshAsync();

		Assert.Equal("rotated", _store.Stored?.SourceToken);
		Assert.Equal("rotated", Assert.Single(_controller.Applied)?.SourceToken);
	}

	[Fact]
	public async Task NoAnswerKeepsWhatTheHandsetHas()
	{
		// Offline, a 500, or a Fellowship older than the route. None of
		// them is the intergroup saying stop.
		_store.Stored = Shipping;
		_client.Answer = null;

		await Build().RefreshAsync();

		Assert.Same(Shipping, _store.Stored);
		Assert.Empty(_controller.Applied);
	}

	[Fact]
	public async Task AnEmptyAnswerIsStoredAsDoNotShip()
	{
		// Stored rather than cleared, so the next launch knows it was told
		// not to ship and does not hold logs waiting for an answer it
		// already has.
		_store.Stored = Shipping;
		_client.Answer = new BetterStackConfiguration();

		await Build().RefreshAsync();

		Assert.NotNull(_store.Stored);
		Assert.False(_store.Stored!.IsValid());
		Assert.False(Assert.Single(_controller.Applied)!.IsValid());
	}

	// ── Signing out ───────────────────────────────────────────────

	[Fact]
	public async Task SigningOutForgetsAndHolds()
	{
		// A token given because the handset was signed in does not
		// outlive the session.
		_store.Stored = Shipping;
		_sessions.Session = null;

		await Build().RefreshAsync();

		Assert.Null(_store.Stored);
		Assert.Null(Assert.Single(_controller.Applied));
		Assert.Null(_client.AskedWith);
	}

	[Fact]
	public async Task ASignedOutLaunchWithNothingStoredRebuildsNothing()
	{
		_sessions.Session = null;

		await Build().RefreshAsync();

		Assert.Empty(_controller.Applied);
	}

	[Fact]
	public void LosingAuthorisationForgetsToo()
	{
		// The sync loop finds out, not the shell, and it announces it
		// rather than calling anything here.
		_store.Stored = Shipping;
		var logging = new RemoteLogging(_client, _sessions, _store, _controller);

		try
		{
			WeakReferenceMessenger.Default.Send(new AuthenticationLost(FellowshipFailure.Unauthenticated, "revoked"));

			Assert.Null(_store.Stored);
			Assert.Null(Assert.Single(_controller.Applied));
		}
		finally
		{
			WeakReferenceMessenger.Default.UnregisterAll(logging);
		}
	}

	// ── The client ────────────────────────────────────────────────

	[Fact]
	public async Task TheClientReadsBothFields()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """
			{"endpoint":"s1.betterstackdata.com","source_token":"src-token"}
			""");

		var config = await Client(handler).FetchLoggingAsync("fdt_secret");

		Assert.NotNull(config);
		Assert.Equal("https://s1.betterstackdata.com", config.Endpoint);
		Assert.Equal("src-token", config.SourceToken);
		Assert.True(config.IsValid());
		Assert.Equal("/wp-json/fellowship/v1/logging", handler.LastUri?.AbsolutePath);
		Assert.Equal("fdt_secret", handler.LastAuthParameter);
	}

	[Fact]
	public async Task AnEmptyAnswerIsAnAnswer()
	{
		var handler = new StubHandler(HttpStatusCode.OK, """{"endpoint":"","source_token":""}""");

		var config = await Client(handler).FetchLoggingAsync("fdt_x");

		Assert.NotNull(config);
		Assert.False(config.IsValid());
	}

	[Theory]
	[InlineData(HttpStatusCode.NotFound)]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task ARefusalIsNoAnswer(HttpStatusCode status)
	{
		// 404 is a Fellowship older than the route.
		var handler = new StubHandler(status, """{"message":"no"}""");

		Assert.Null(await Client(handler).FetchLoggingAsync("fdt_x"));
	}

	[Fact]
	public async Task NoNetworkIsNoAnswer()
	{
		var client = new FellowshipClient(
			new HttpClient(new ThrowingHandler(new HttpRequestException("no route to host"))),
			new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org" });

		Assert.Null(await client.FetchLoggingAsync("fdt_x"));
	}

	// ── Holding ───────────────────────────────────────────────────

	[Fact]
	public async Task HoldingKeepsEveryBatch()
	{
		// Anything but a 2xx tells the durable sink to keep the batch on
		// disk, which is what holding means.
		using var client = new HoldingHttpClient();
		using var body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

		using var response = await client.PostAsync("https://holding.invalid/", body, CancellationToken.None);

		Assert.False(response.IsSuccessStatusCode);
	}

	// ── Fixtures ──────────────────────────────────────────────────

	private RemoteLogging Build()
	{
		var logging = new RemoteLogging(_client, _sessions, _store, _controller);

		// Other tests send AuthenticationLost through the same process-wide
		// messenger. Only the test about that message should hear it.
		WeakReferenceMessenger.Default.UnregisterAll(logging);

		return logging;
	}

	private static FellowshipClient Client(HttpMessageHandler handler) =>
		new(new HttpClient(handler), new FellowshipConfiguration { BaseUrl = "https://aa-bristol.org" });

	private sealed class FakeController : ILogShipper
	{
		public ShippingState State => ShippingState.NotStarted;

		public List<BetterStackConfiguration?> Applied { get; } = [];

		public void Reconfigure(BetterStackConfiguration? config) => Applied.Add(config);

		public void Flush()
		{
		}
	}

	private sealed class FakeStore : ILoggingSettingsStore
	{
		public BetterStackConfiguration? Stored { get; set; }

		public Task<BetterStackConfiguration?> LoadAsync() => Task.FromResult(Stored);

		public Task SaveAsync(BetterStackConfiguration configuration)
		{
			Stored = configuration;
			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			Stored = null;
			return Task.CompletedTask;
		}
	}

	private sealed class FakeSessions : ISessionStore
	{
		public DeviceSession? Session { get; set; } = new() { Token = "fdt_test", DeviceId = 1, MemberId = 2 };

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

	/// <summary>
	/// Answers the logging route and nothing else. Everything else on the
	/// interface belongs to other tests.
	/// </summary>
	private sealed class FakeClient : IFellowshipClient
	{
		public BetterStackConfiguration? Answer { get; set; }

		public string? AskedWith { get; private set; }

		public Task<BetterStackConfiguration?> FetchLoggingAsync(string token, CancellationToken cancellationToken = default)
		{
			AskedWith = token;
			return Task.FromResult(Answer);
		}

		public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

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

	private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		public Uri? LastUri { get; private set; }

		public string? LastAuthParameter { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastUri = request.RequestUri;
			LastAuthParameter = request.Headers.Authorization?.Parameter;

			return Task.FromResult(new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			});
		}
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromException<HttpResponseMessage>(exception);
	}
}
