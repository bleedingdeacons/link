using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Opens a message sealed by Fellowship.
///
/// <para>This is the handset's half of a contract whose other half is
/// PHP, and the two ends agree only if a long list of details match. The
/// server side is <c>Fellowship\Crypto\MessageSealer</c>; its unit test
/// opens an envelope the way this class does, and this class's test
/// builds one the way that class does. <b>Each side's test does the other
/// side's job, so drift on either turns the opposite one red. If one
/// changes, both change.</b></para>
///
/// <para><b>Hybrid, because RSA cannot carry a message.</b> An RSA-2048
/// key with OAEP padding encrypts 214 bytes and no more. So the server
/// makes a fresh 32-byte content key per message, seals the body under it
/// with AES-256-GCM, and encrypts only the content key to this handset's
/// public key. Two fields arrive: <c>k</c>, the wrapped content key, and
/// <c>p</c>, the sealed payload.</para>
///
/// <para><b>OAEP with SHA-1, deliberately, and it is not a weakness.</b>
/// PHP's <c>openssl_public_encrypt()</c> with
/// <c>OPENSSL_PKCS1_OAEP_PADDING</c> uses SHA-1 for the OAEP hash and
/// MGF1, with no way to ask it for SHA-256 short of an API PHP does not
/// expose. SHA-1's collision weakness is a signature problem; OAEP relies
/// on preimage resistance, which is intact. Changing this to
/// <see cref="RSAEncryptionPadding.OaepSHA256"/> without changing the
/// server produces a message that arrives and silently will not open.
/// <b>This is the single line here most likely to be "improved" into a
/// bug.</b></para>
///
/// <para><b>What this buys over Reach's symmetric scheme.</b> Reach
/// issues each handset a key and keeps a copy, so its server can open
/// anything it ever sent. Here the private half never leaves this device
/// — it is generated at enrolment and only the public half is sent — so a
/// payload Fellowship sealed yesterday is one Fellowship cannot open
/// today. The cost is that a lost key cannot be recovered, only replaced.
/// </para>
///
/// <para>The rest is what Hand does: AES-256-GCM over gzip, with the
/// envelope the server packs — 12 bytes of nonce, then the 16-byte tag,
/// then the ciphertext, all base64. GCM authenticates, so a payload
/// altered in transit fails to open rather than decrypting to something
/// plausible. The gzip is not for tidiness — sealing and base64'ing the
/// largest payload the server will accept overflows FCM's 4KB limit
/// without it.</para>
/// </summary>
public static class MessagePayloadCipher
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int ContentKeyBytes = 32;

	/// <summary>
	/// The payload the envelope carries, or null when it cannot be opened.
	///
	/// <para>Null covers every reason at once — a private key this handset
	/// no longer has, a key that does not match the one this was sealed
	/// to, a truncated payload, a tampered one, something that
	/// decompresses to a shape this does not recognise — because the
	/// caller can do nothing different about any of them. What it does
	/// about all of them is the same: keep the message out of the list,
	/// and tell Fellowship this handset cannot read its messages, so the
	/// Devices screen can show somebody.</para>
	/// </summary>
	/// <param name="wrappedKey">The <c>k</c> field: the content key, RSA-OAEP to this device, base64.</param>
	/// <param name="sealedPayload">The <c>p</c> field: nonce, tag, ciphertext, base64.</param>
	/// <param name="privateKeyPem">This handset's private key, PKCS#8 PEM.</param>
	public static Dictionary<string, JsonElement>? Open(string wrappedKey, string sealedPayload, string privateKeyPem)
	{
		if (string.IsNullOrEmpty(wrappedKey) || string.IsNullOrEmpty(sealedPayload) || string.IsNullOrEmpty(privateKeyPem))
		{
			return null;
		}

		if (!TryDecodeBase64(wrappedKey, out var wrapped) || !TryDecodeBase64(sealedPayload, out var packed))
		{
			return null;
		}

		if (packed.Length <= NonceBytes + TagBytes)
		{
			return null;
		}

		var contentKey = Unwrap(wrapped, privateKeyPem);
		if (contentKey is null || contentKey.Length != ContentKeyBytes)
		{
			return null;
		}

		var nonce = packed.AsSpan(0, NonceBytes);
		var tag = packed.AsSpan(NonceBytes, TagBytes);
		var body = packed.AsSpan(NonceBytes + TagBytes);
		var compressed = new byte[body.Length];

		try
		{
			using var gcm = new AesGcm(contentKey, TagBytes);
			gcm.Decrypt(nonce, body, tag, compressed);
		}
		catch (CryptographicException)
		{
			// The tag did not verify: a wrong key, or a payload that was
			// altered on the way. Both mean the same thing here.
			return null;
		}
		finally
		{
			// The content key is single-use and there is no reason for it
			// to outlive the message it opened.
			CryptographicOperations.ZeroMemory(contentKey);
		}

		var json = Inflate(compressed);
		if (json is null)
		{
			return null;
		}

		try
		{
			var opened = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

			// Ordinal, matching the keys the server writes. A
			// case-insensitive lookup would be a quiet invitation for two
			// fields to collide.
			return opened is null ? null : new Dictionary<string, JsonElement>(opened, StringComparer.Ordinal);
		}
		catch (JsonException)
		{
			// Decrypted and decompressed to something that is not an
			// object. Only reachable if the two ends disagree about the
			// format, which is worth failing softly rather than throwing
			// out of a push handler.
			return null;
		}
	}

	/// <summary>
	/// Recover the content key, or null if this handset cannot.
	/// </summary>
	private static byte[]? Unwrap(byte[] wrapped, string privateKeyPem)
	{
		try
		{
			using var rsa = RSA.Create();
			rsa.ImportFromPem(privateKeyPem);

			// OaepSHA1 to match PHP — see the class remarks.
			return rsa.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA1);
		}
		catch (CryptographicException)
		{
			// The key will not import, or will not open this. A replaced
			// keypair reaches here for every message sealed to the old one,
			// which is expected and is why it is not an exception the
			// caller has to handle.
			return null;
		}
		catch (ArgumentException)
		{
			// ImportFromPem throws this for a string that is not PEM at all
			// — a cleared keystore reading back as something odd.
			return null;
		}
	}

	/// <summary>
	/// Undo the server's <c>gzencode</c>, or null if it will not undo.
	/// </summary>
	private static byte[]? Inflate(byte[] compressed)
	{
		try
		{
			using var source = new MemoryStream(compressed, writable: false);
			using var gzip = new GZipStream(source, CompressionMode.Decompress);
			using var inflated = new MemoryStream();

			gzip.CopyTo(inflated);

			return inflated.ToArray();
		}
		catch (InvalidDataException)
		{
			// Decrypted cleanly but is not gzip. The tag verified, so this
			// is the two ends disagreeing about the format rather than
			// anything an attacker did.
			return null;
		}
	}

	private static bool TryDecodeBase64(string value, out byte[] bytes)
	{
		var buffer = new byte[((value.Length * 3) + 3) / 4];

		if (Convert.TryFromBase64String(value, buffer, out var written))
		{
			bytes = buffer[..written];
			return true;
		}

		bytes = [];
		return false;
	}
}
