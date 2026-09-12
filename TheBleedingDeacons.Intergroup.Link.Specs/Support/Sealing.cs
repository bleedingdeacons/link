using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Support;

/// <summary>
/// The server side of a message, so a scenario can send one.
///
/// <para><b>It seals by construction rather than by calling Link's own
/// code.</b> Gzip, then AES-256-GCM under a fresh 32-byte content key,
/// nonce then tag then ciphertext; the content key RSA-OAEP to this
/// handset's public half — written out here the way Fellowship's
/// <c>MessageSealer</c> writes it. Sealing with the code under test would
/// pass just as happily if both ends of the format changed together, and
/// that is the one failure this has to catch: the two halves live in
/// different repositories and ship on different days.</para>
///
/// <para><b>OaepSHA1 is a transcription, not an opinion.</b> It is the
/// only OAEP PHP's <c>openssl_public_encrypt()</c> performs. Changing it
/// here produces envelopes no shipping handset can open, and the feature
/// file that says so is the one that would go red.</para>
///
/// <para>It is a second copy of Link.Tests' <c>Sealing</c>, which is
/// unavoidable rather than sloppy: that one is <c>internal</c> to a test
/// project, and nothing can reference a test project. Keeping this lean
/// is the mitigation — it covers what the feature files ask for and
/// nothing else.</para>
/// </summary>
public static class Sealing
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int ContentKeyBytes = 32;

	/// <summary>The key size Fellowship's floor requires.</summary>
	public const int KeyBits = 2048;

	/// <summary>A handset's keypair, as enrolment leaves it.</summary>
	public sealed record Keypair
	{
		/// <summary>The half that goes to Fellowship, base64 SPKI.</summary>
		public required string PublicKey { get; init; }

		/// <summary>The half that never leaves, PKCS#8 PEM.</summary>
		public required string PrivateKeyPem { get; init; }
	}

	/// <summary>A keypair of the shape <c>DeviceKeyStore</c> generates.</summary>
	public static Keypair NewKeypair()
	{
		using var rsa = RSA.Create(KeyBits);

		return new Keypair
		{
			PublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
			PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
		};
	}

	/// <summary>
	/// What Fellowship seals: the whole message, inside the one blob.
	/// </summary>
	public static Dictionary<string, object> Payload(long id) =>
		new(StringComparer.Ordinal)
		{
			["id"] = id,
			["uuid"] = $"message-{id}",
			["subject"] = "Intergroup meeting moved",
			["body"] = "September intergroup is now the 14th, same room.",
			["sender"] = "Dave B",
			["created_at"] = 1788000000L,
			["reply_to"] = 0,
			["read_at"] = 0,
		};

	/// <summary>
	/// One envelope, as the client hands it on: an id in the clear and two
	/// sealed fields.
	/// </summary>
	public static SealedMessage Seal(long id, IDictionary<string, object> payload, string base64PublicKey)
	{
		ArgumentNullException.ThrowIfNull(payload);

		using var rsa = RSA.Create();
		rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64PublicKey), out _);

		var compressed = Gzip(JsonSerializer.SerializeToUtf8Bytes(payload));

		// A fresh content key per message: GCM fails catastrophically on a
		// repeated key and nonce pair, and never keeping one is what
		// guarantees it never repeats.
		var contentKey = RandomNumberGenerator.GetBytes(ContentKeyBytes);
		var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
		var ciphertext = new byte[compressed.Length];
		var tag = new byte[TagBytes];

		using (var gcm = new AesGcm(contentKey, TagBytes))
		{
			gcm.Encrypt(nonce, compressed, ciphertext, tag);
		}

		return new SealedMessage
		{
			Id = id,
			WrappedKey = Convert.ToBase64String(rsa.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA1)),
			Payload = Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]),
		};
	}

	/// <summary>
	/// The same envelope with one byte of its ciphertext altered, which
	/// GCM's tag is there to catch.
	/// </summary>
	public static SealedMessage Tamper(SealedMessage envelope)
	{
		ArgumentNullException.ThrowIfNull(envelope);

		var packed = Convert.FromBase64String(envelope.Payload);

		// The last byte, which is ciphertext rather than nonce or tag —
		// so what fails is the authentication of the body itself.
		packed[^1] ^= 0xFF;

		return envelope with { Payload = Convert.ToBase64String(packed) };
	}

	/// <summary>PHP's <c>gzencode</c>: gzip, not raw deflate.</summary>
	private static byte[] Gzip(byte[] raw)
	{
		using var compressed = new MemoryStream();

		using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			gzip.Write(raw, 0, raw.Length);
		}

		return compressed.ToArray();
	}
}
