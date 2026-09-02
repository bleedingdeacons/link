using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Fetches, opens and stores messages.
///
/// <para>This is the app's centre of gravity, and it lives in Link.Core
/// rather than in the app for the same reason Hand's alert loop does: it
/// is the part most worth testing, and a class that reached for
/// <c>SecureStorage</c> or <c>MainThread</c> could not be. Everything
/// platform-shaped arrives through an interface.</para>
///
/// <para><b>Push and poll converge here deliberately.</b> A pushed
/// envelope and a polled one are the same bytes in the same format, so
/// there is one <see cref="Open"/> and one path into the history. The
/// alternative — a push handler that knows how to decrypt — is how the
/// two routes drift apart until a message displays differently depending
/// on how it arrived.</para>
///
/// <para><b>Push is the fast path, not the reliable one.</b> Nothing here
/// assumes a push arrived. A sync fetches everything above the highest id
/// held, so a message whose push was dropped, delayed by Doze, or sent to
/// a rotated FCM token is picked up on the next pass regardless.</para>
/// </summary>
public sealed class MessageService : IMessageService
{
	private readonly IFellowshipClient _client;
	private readonly IMessageHistory _history;
	private readonly IDeviceKeyStore _keys;
	private readonly ISessionStore _sessions;

	public MessageService(
		IFellowshipClient client,
		IMessageHistory history,
		IDeviceKeyStore keys,
		ISessionStore sessions)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_history = history ?? throw new ArgumentNullException(nameof(history));
		_keys = keys ?? throw new ArgumentNullException(nameof(keys));
		_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
	}

	public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return SyncResult.Failed;
		}

		var since = await _history.HighestIdAsync(cancellationToken).ConfigureAwait(false);

		var page = await _client.FetchInboxAsync(session.Token, since, cancellationToken).ConfigureAwait(false);
		if (!page.Succeeded)
		{
			return SyncResult.Failed;
		}

		if (page.Messages.Count == 0)
		{
			return new SyncResult { Received = 0, Unread = page.Unread };
		}

		var privateKey = await _keys.PrivateKeyAsync().ConfigureAwait(false);

		var opened = new List<LinkMessage>();
		var unopened = 0;

		foreach (var envelope in page.Messages)
		{
			var message = Open(envelope, privateKey);

			if (message is null)
			{
				unopened++;
			}
			else
			{
				opened.Add(message);
			}
		}

		if (opened.Count > 0)
		{
			await _history.SaveAsync(opened, cancellationToken).ConfigureAwait(false);
		}

		if (opened.Count > 0)
		{
			Log.Information("Sync stored {Count} message(s); {Unread} unread", opened.Count, page.Unread);
		}

		if (unopened > 0)
		{
			// Told once per sync, not once per message: a handset whose
			// key has gone reports a fault, it does not report fifty.
			Log.Error(
				"{Unopened} of {Total} message(s) could not be opened; reporting a key fault",
				unopened,
				page.Messages.Count);

			await _client.ReportKeyFaultAsync(session.Token, cancellationToken).ConfigureAwait(false);
		}

		return new SyncResult
		{
			Received = opened.Count,
			Unread = page.Unread,
			KeyFault = unopened > 0,
		};
	}

	public async Task<LinkMessage?> ReceivePushAsync(
		string wrappedKey,
		string sealedPayload,
		CancellationToken cancellationToken = default)
	{
		var privateKey = await _keys.PrivateKeyAsync().ConfigureAwait(false);

		var message = Open(
			new SealedMessage { Id = 1, WrappedKey = wrappedKey, Payload = sealedPayload },
			privateKey);
		if (message is null)
		{
			// Quiet on screen, not quiet in the log — those are different
			// decisions, and only the first one was ever intended. A push
			// that will not open is not a notification worth raising — the member would get "New
			// message" for something the app cannot show them — and the
			// next sync reports the key fault properly, with a session
			// token to hand. But it is the clearest evidence there is that
			// this handset's key and the intergroup's copy have parted
			// company, and before there was a log it left none.
			Log.Error(
				"A pushed message could not be opened{Detail}",
				string.IsNullOrEmpty(privateKey) ? "; this handset holds no private key" : string.Empty);

			return null;
		}

		await _history.SaveAsync([message], cancellationToken).ConfigureAwait(false);

		// Tell whoever is on screen. Without this the list goes on showing
		// what it loaded when the page appeared, which is wrong in exactly
		// the case that matters most — a message arriving while somebody is
		// looking at it.
		//
		// Sent after the save, so a subscriber that reloads from the history
		// finds the message there. Sent on this thread, which is a
		// background one and possibly the push service's: subscribers
		// marshal to the UI themselves rather than this guessing there is a
		// UI to marshal to.
		WeakReferenceMessenger.Default.Send(new MessageReceived(message));

		return message;
	}

	public async Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default)
	{
		// Locally first, so the list stops showing it as unread whether or
		// not the network is there. The server is the authority on read
		// state across a member's devices, but it is not the authority on
		// whether this one should redraw.
		await _history.MarkReadAsync(messageId, cancellationToken).ConfigureAwait(false);

		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return;
		}

		await _client.MarkReadAsync(session.Token, messageId, cancellationToken).ConfigureAwait(false);
	}

	public async Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return SendResult.Failed("This device is not signed in.");
		}

		return await _client.SendAsync(session.Token, request, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// One envelope in, one message out — or null, which the callers
	/// count rather than inspect.
	/// </summary>
	private static LinkMessage? Open(SealedMessage envelope, string privateKey)
	{
		if (string.IsNullOrEmpty(privateKey))
		{
			// No key at all: a factory reset, a restored backup, a cleared
			// keystore. Counted as unopened like any other failure, so the
			// key fault gets reported by the same path.
			return null;
		}

		var payload = MessagePayloadCipher.Open(envelope.WrappedKey, envelope.Payload, privateKey);

		return payload is null ? null : LinkMessage.FromPayload(payload);
	}
}
