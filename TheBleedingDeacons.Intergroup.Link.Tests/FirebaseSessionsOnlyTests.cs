using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// A session from before the move to Firebase.
///
/// <para>Its token is a credential for the intergroup's own site. What matters is that it
/// reaches nothing, and that the member is told why they have to sign in again.</para>
/// </summary>
public sealed class FirebaseSessionsOnlyTests : IDisposable
{
	private static readonly DeviceSession OldSession = new() { Token = "fdt_old", DeviceId = 12, MemberId = 7 };

	private static readonly DeviceSession NewSession = new() { Token = "refresh", FirebaseDeviceId = "d_abc", MemberId = 7 };

	private readonly Sessions _inner = new();
	private readonly Keys _keys = new();
	private readonly WeakReferenceMessenger _messenger = new();
	private readonly object _listener = new();
	private readonly List<AuthenticationLost> _lost = [];

	public FirebaseSessionsOnlyTests() =>
		_messenger.Register<AuthenticationLost>(_listener, (_, lost) => _lost.Add(lost));

	public void Dispose() => _messenger.UnregisterAll(_listener);

	private FirebaseSessionsOnly Store() => new(_inner, _keys, _messenger);

	[Fact]
	public async Task ASessionOnFirebaseIsLeftAlone()
	{
		_inner.Session = NewSession;
		var store = Store();

		Assert.Equal(NewSession, await store.LoadAsync());
		Assert.False(store.RetiredOne);
		Assert.Equal(0, _keys.Cleared);
		Assert.Empty(_lost);
	}

	[Fact]
	public async Task NoSessionIsNothingToRetire()
	{
		var store = Store();

		Assert.Null(await store.LoadAsync());
		Assert.False(store.RetiredOne);
		Assert.Empty(_lost);
	}

	[Fact]
	public async Task AnOldSessionNeverLeavesAndIsCleared()
	{
		_inner.Session = OldSession;
		var store = Store();

		Assert.Null(await store.LoadAsync());
		Assert.Null(_inner.Session);
		Assert.Equal(1, _keys.Cleared);
		Assert.True(store.RetiredOne);
	}

	[Fact]
	public async Task TheMemberIsToldOnceAndTheirMessagesAreMentioned()
	{
		_inner.Session = OldSession;
		var store = Store();

		await store.LoadAsync();
		await store.LoadAsync();

		var lost = Assert.Single(_lost);
		Assert.Equal(FellowshipFailure.Unauthenticated, lost.Failure);
		Assert.Contains("messages are still here", lost.Reason, StringComparison.Ordinal);
		Assert.Equal(1, _keys.Cleared);
	}

	[Fact]
	public async Task SavingAndClearingPassThrough()
	{
		var store = Store();

		await store.SaveAsync(NewSession);
		Assert.Equal(NewSession, _inner.Session);

		await store.ClearAsync();
		Assert.Null(_inner.Session);
	}

	private sealed class Sessions : ISessionStore
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

	private sealed class Keys : IDeviceKeyStore
	{
		public int Cleared { get; private set; }

		public Task<bool> HasKeyAsync() => Task.FromResult(true);

		public Task<string> RegenerateAsync() => Task.FromResult(string.Empty);

		public Task<string> PublicKeyAsync() => Task.FromResult(string.Empty);

		public Task<string> PrivateKeyAsync() => Task.FromResult(string.Empty);

		public Task ClearAsync()
		{
			Cleared++;

			return Task.CompletedTask;
		}
	}
}
