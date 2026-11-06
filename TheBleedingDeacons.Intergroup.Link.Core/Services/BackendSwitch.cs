using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Decides which back end this phone uses, by asking the site.
///
/// <para><b>Only a clear answer changes anything.</b> No network, an outage or a
/// page from something in front of the site all leave the phone where it was. So
/// a phone stays on Fellowship until told otherwise, and one on Firebase isn't
/// signed out because WordPress had a bad minute.</para>
///
/// <para><b>A session belongs to one back end.</b> Its token means nothing to the
/// other, so when the answer changes the phone signs out and asks its member to
/// sign in again. The history stays. The keypair goes, because a sign-in makes a
/// new one anyway, and nothing moves between the back ends.</para>
/// </summary>
public sealed class BackendSwitch
{
	private const string MovedReason =
		"The intergroup has changed how Link connects, so this phone has been signed out. "
		+ "Your messages are still here. Sign in again to carry on receiving new ones.";

	private readonly IBackendSource _source;
	private readonly IBackendStore _store;
	private readonly ISessionStore _sessions;
	private readonly IDeviceKeyStore _keys;
	private readonly Func<IMessageHistory> _history;
	private readonly Func<bool> _firebaseReady;
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <param name="source">The site.</param>
	/// <param name="store">The last answer.</param>
	/// <param name="sessions">This phone's session.</param>
	/// <param name="keys">This phone's keypair.</param>
	/// <param name="history">Late, because building the history touches secure storage.</param>
	/// <param name="firebaseReady">
	/// Whether this build can talk to Firebase at all. One built without its Firebase
	/// configuration stays where it is, because moving would leave it unable to sign in.
	/// </param>
	public BackendSwitch(
		IBackendSource source,
		IBackendStore store,
		ISessionStore sessions,
		IDeviceKeyStore keys,
		Func<IMessageHistory> history,
		Func<bool> firebaseReady)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
		_keys = keys ?? throw new ArgumentNullException(nameof(keys));
		_history = history ?? throw new ArgumentNullException(nameof(history));
		_firebaseReady = firebaseReady ?? throw new ArgumentNullException(nameof(firebaseReady));
	}

	/// <summary>The back end to talk to right now.</summary>
	public LinkBackend Current => _store.Backend;

	/// <summary>Ask the site, and follow its answer.</summary>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		var answer = await _source.FetchBackendAsync(cancellationToken).ConfigureAwait(false);
		if (answer is not { } wanted)
		{
			return;
		}

		if (wanted == LinkBackend.Firebase && !_firebaseReady())
		{
			Log.Warning("The site says to use Firebase, but this build has no Firebase configuration; staying put");
			return;
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await FollowAsync(wanted, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task FollowAsync(LinkBackend wanted, CancellationToken cancellationToken)
	{
		var previous = _store.Backend;
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		var signedIn = session is { IsSignedIn: true };
		var mismatched = signedIn && session!.Backend != wanted;

		if (previous == wanted && !mismatched)
		{
			return;
		}

		Log.Information("Moving this phone from {Previous} to {Wanted}", previous, wanted);

		// In this order, so a crash part way leaves a phone that is signed out and
		// asks again, and never one sending a token to the wrong back end.
		if (mismatched)
		{
			await _sessions.ClearAsync().ConfigureAwait(false);
			await _keys.ClearAsync().ConfigureAwait(false);
		}

		if (wanted == LinkBackend.Fellowship)
		{
			await _history().ReturnToFellowshipAsync(cancellationToken).ConfigureAwait(false);
		}

		_store.Backend = wanted;

		if (mismatched)
		{
			WeakReferenceMessenger.Default.Send(new AuthenticationLost(FellowshipFailure.Unauthenticated, MovedReason));
		}
	}
}
