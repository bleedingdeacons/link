using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Retires a session left over from before Link moved to Firebase.
///
/// <para><b>Here, in front of every read of the session,</b> so an old token can't reach
/// anything. It is a credential for the intergroup's own site, and handing it to Google to
/// be refused would be sending it somewhere it was never meant to go.</para>
///
/// <para>The phone signs out once and asks its member to sign in again. Its messages stay.
/// The keypair goes, because a sign-in makes a new one anyway.</para>
/// </summary>
public sealed class FirebaseSessionsOnly : ISessionStore
{
	/// <summary>What the member is told.</summary>
	public const string Reason =
		"Link has been updated, so this phone needs signing in again. "
		+ "Your messages are still here.";

	private readonly ISessionStore _inner;
	private readonly IDeviceKeyStore _keys;
	private readonly IMessenger _messenger;

	public FirebaseSessionsOnly(ISessionStore inner, IDeviceKeyStore keys, IMessenger? messenger = null)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_keys = keys ?? throw new ArgumentNullException(nameof(keys));
		_messenger = messenger ?? WeakReferenceMessenger.Default;
	}

	/// <summary>Whether this launch signed the phone out, so the sign-in screen can say why.</summary>
	public bool RetiredOne { get; private set; }

	public async Task<DeviceSession?> LoadAsync()
	{
		var session = await _inner.LoadAsync().ConfigureAwait(false);

		if (session is not { IsSignedIn: true } || session.FirebaseDeviceId.Length > 0)
		{
			return session;
		}

		Log.Information("Retiring a session from before Link moved to Firebase");

		await _inner.ClearAsync().ConfigureAwait(false);
		await _keys.ClearAsync().ConfigureAwait(false);

		RetiredOne = true;
		_messenger.Send(new AuthenticationLost(FellowshipFailure.Unauthenticated, Reason));

		return null;
	}

	public Task SaveAsync(DeviceSession session) => _inner.SaveAsync(session);

	public Task ClearAsync() => _inner.ClearAsync();
}
