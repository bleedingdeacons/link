using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The on-device message history: one encrypted JSON file.
///
/// <para><b>One file rather than a database.</b> This holds a few hundred
/// short messages at most and is read whole every time it is read at all
/// — there is no query here that a SQLite dependency would make faster,
/// and there is a real cost to the alternative: a database file is
/// awkward to encrypt as a unit, and the point of this class is that what
/// sits on the flash is unreadable.</para>
///
/// <para><b>Encrypted at rest, with the same envelope as everything
/// else.</b> AES-256-GCM, 12-byte nonce, 16-byte tag. The key comes from
/// the platform's secure storage and is passed in, so this class has no
/// opinion about where it lives and a test can hand it 32 bytes.</para>
///
/// <para><b>Corruption reads as "no history", never as an error.</b> A
/// file that will not decrypt — a changed key, a half-written file after
/// a battery pull — must not stop the app opening. The member loses a
/// local cache that the server can mostly refill, which is a far better
/// outcome than a messaging app that will not start.</para>
///
/// <para>Writes are serialised through a semaphore because a push
/// handler and the foreground poll can both save at once, and the loser
/// of that race would otherwise truncate the file.</para>
/// </summary>
public sealed class JsonMessageHistory : IMessageHistory, IDisposable
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int KeyBytes = 32;

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly string _path;
	private readonly byte[] _key;
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <param name="path">The file to keep. Its directory is created if it does not exist.</param>
	/// <param name="key">32 bytes from the platform's secure storage.</param>
	public JsonMessageHistory(string path, byte[] key)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		ArgumentNullException.ThrowIfNull(key);

		if (key.Length != KeyBytes)
		{
			// A wrong-length key is a wiring mistake in the app, not a
			// runtime condition — and a history silently written under a
			// truncated key would be the kind of thing nobody notices
			// until it matters.
			throw new ArgumentException($"The history key must be {KeyBytes} bytes.", nameof(key));
		}

		_path = path;
		_key = key;
	}

	public async Task<IReadOnlyList<LinkMessage>> AllAsync(CancellationToken cancellationToken = default)
	{
		var held = await ReadAsync(cancellationToken).ConfigureAwait(false);

		return held.Values.OrderByDescending(m => m.Id).ToList();
	}

	public async Task SaveAsync(IEnumerable<LinkMessage> messages, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messages);

		var incoming = messages.Where(m => m.Id > 0).ToList();
		if (incoming.Count == 0)
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var held = await ReadUnguardedAsync(cancellationToken).ConfigureAwait(false);

			foreach (var message in incoming)
			{
				// Replace rather than skip. The same message arrives by
				// push and again by poll, and only the poll's copy carries
				// the read flag — a store that ignored the second copy
				// would show a message as unread forever after it was read
				// on another device.
				//
				// The one thing not to lose is a local read that has not
				// reached the server yet, which is why a locally-read
				// message keeps its flag when the incoming copy is unread.
				if (held.TryGetValue(message.Id, out var existing) && existing.IsRead && !message.IsRead)
				{
					held[message.Id] = message with { ReadAt = existing.ReadAt };
				}
				else
				{
					held[message.Id] = message;
				}
			}

			await WriteUnguardedAsync(held, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<long> HighestIdAsync(CancellationToken cancellationToken = default)
	{
		var held = await ReadAsync(cancellationToken).ConfigureAwait(false);

		return held.Count == 0 ? 0 : held.Keys.Max();
	}

	public async Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var held = await ReadUnguardedAsync(cancellationToken).ConfigureAwait(false);

			if (!held.TryGetValue(messageId, out var message) || message.IsRead)
			{
				return;
			}

			held[messageId] = message with { ReadAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

			await WriteUnguardedAsync(held, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Delete the store.
	///
	/// <para>This clears the handset's copy and nothing else. It does not
	/// unsend anything, other people still have theirs, and a message the
	/// server has not yet aged out will arrive again on the next poll —
	/// which is why the screen offering this says so rather than
	/// implying otherwise.</para>
	/// </summary>
	public async Task ClearAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (File.Exists(_path))
			{
				File.Delete(_path);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The file is locked, or gone already. Either way there is
			// nothing useful to do and nothing worth failing the member's
			// tap over — they asked for the history to be gone, and from
			// where they are standing it is.
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	private async Task<Dictionary<long, LinkMessage>> ReadAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			return await ReadUnguardedAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<Dictionary<long, LinkMessage>> ReadUnguardedAsync(CancellationToken cancellationToken)
	{
		if (!File.Exists(_path))
		{
			return [];
		}

		byte[] packed;

		try
		{
			packed = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return [];
		}

		if (packed.Length <= NonceBytes + TagBytes)
		{
			return [];
		}

		var plaintext = new byte[packed.Length - NonceBytes - TagBytes];

		try
		{
			using var gcm = new AesGcm(_key, TagBytes);
			gcm.Decrypt(
				packed.AsSpan(0, NonceBytes),
				packed.AsSpan(NonceBytes + TagBytes),
				packed.AsSpan(NonceBytes, TagBytes),
				plaintext);
		}
		catch (CryptographicException)
		{
			// A changed key, or a file that was half-written. Reads as no
			// history rather than as an error — see the class remarks.
			return [];
		}

		try
		{
			var messages = JsonSerializer.Deserialize<List<LinkMessage>>(plaintext, JsonOptions);

			return messages is null
				? []
				: messages.Where(m => m.Id > 0).ToDictionary(m => m.Id);
		}
		catch (Exception e) when (e is JsonException or ArgumentException)
		{
			// ArgumentException covers a duplicate id in a file written by
			// something that did not go through SaveAsync.
			return [];
		}
	}

	private async Task WriteUnguardedAsync(Dictionary<long, LinkMessage> held, CancellationToken cancellationToken)
	{
		var json = JsonSerializer.SerializeToUtf8Bytes(held.Values.OrderByDescending(m => m.Id).ToList(), JsonOptions);

		var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
		var ciphertext = new byte[json.Length];
		var tag = new byte[TagBytes];

		using (var gcm = new AesGcm(_key, TagBytes))
		{
			// A fresh nonce every write. GCM fails catastrophically on a
			// repeated key/nonce pair, and this file is rewritten every
			// time a message arrives.
			gcm.Encrypt(nonce, json, ciphertext, tag);
		}

		var packed = new byte[NonceBytes + TagBytes + ciphertext.Length];
		nonce.CopyTo(packed, 0);
		tag.CopyTo(packed, NonceBytes);
		ciphertext.CopyTo(packed, NonceBytes + TagBytes);

		var directory = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		try
		{
			// Written beside the target and moved into place, so a process
			// killed mid-write leaves the previous history intact rather
			// than a truncated file that reads as no history at all.
			var temporary = _path + ".tmp";

			await File.WriteAllBytesAsync(temporary, packed, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, _path, overwrite: true);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// The history is a convenience, not the record. Losing a write
			// costs the member a message that the next poll will fetch
			// again; throwing here would take a push handler down with it.
		}
	}

	/// <summary>
	/// Turn a stored base64 key into bytes, generating a new one if there
	/// is nothing usable to turn.
	///
	/// <para>Lives here rather than in the app so the rule about what
	/// counts as usable is written once, next to the code that depends on
	/// it. A regenerated key means the existing history will not decrypt
	/// and reads as empty — which is correct: without the old key it is
	/// unreadable bytes, and the alternative is refusing to start.</para>
	/// </summary>
	public static byte[] KeyFrom(string? stored, out string toStore)
	{
		if (!string.IsNullOrEmpty(stored))
		{
			var buffer = new byte[((stored.Length * 3) + 3) / 4];

			if (Convert.TryFromBase64String(stored, buffer, out var written) && written == KeyBytes)
			{
				toStore = stored;
				return buffer[..written];
			}
		}

		var fresh = RandomNumberGenerator.GetBytes(KeyBytes);
		toStore = Convert.ToBase64String(fresh);

		return fresh;
	}

	/// <summary>
	/// Only used by the tests, to build a store with a known key without
	/// reaching for <see cref="Encoding"/> gymnastics at each call site.
	/// </summary>
	internal static byte[] KeyForTesting(byte fill) => Enumerable.Repeat(fill, KeyBytes).ToArray();
}
