using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Seals a payload the way Fellowship's PHP sealer does.
///
/// <para><b>Why the test builds the envelope rather than loading one.</b>
/// The obvious alternative is a fixture produced by running the real PHP
/// and committing the result. That was tried and dropped: the fixture has
/// to carry the private key that opens it, and a private key in a public
/// repository is what GitHub's push protection blocks and Semgrep flags —
/// and a committed fixture is frozen at whatever moment somebody made
/// it.</para>
///
/// <para>What keeps the two ends honest instead is that the format is
/// written out twice, deliberately, and <b>each side's test does the
/// other side's job</b>. This class is PHP's
/// <c>MessageSealer::seal()</c> in C#; Fellowship's
/// <c>MessageSealerTest</c> is <c>MessagePayloadCipher.Open()</c> in PHP.
/// If either implementation drifts, the test on the *other* side of it
/// goes red. Reach and Hand keep their contract honest the same way,
/// which is the stronger argument for it: this is not a new idea being
/// tried here.</para>
///
/// <para>So the details below — the 12-byte nonce, the 16-byte tag, the
/// 32-byte content key, the gzip, and above all
/// <see cref="RSAEncryptionPadding.OaepSHA1"/> — are not this file's
/// opinion. They are a transcription, and changing one here without
/// changing the PHP produces a passing test and a broken phone.</para>
/// </summary>
internal static class Sealing
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int ContentKeyBytes = 32;

	/// <summary>Key size Fellowship's floor requires.</summary>
	public const int KeyBits = 2048;

	/// <summary>A sealed envelope and the private key that opens it.</summary>
	internal sealed record Sealed
	{
		public required string WrappedKey { get; init; }

		public required string Payload { get; init; }

		public required string PrivateKeyPem { get; init; }

		/// <summary>The envelope in the shape the REST client hands back.</summary>
		public SealedMessage Envelope(long id = ExpectedId) => new()
		{
			Id = id,
			WrappedKey = WrappedKey,
			Payload = Payload,
		};
	}

	/// <summary>The message id <see cref="Payload"/> carries.</summary>
	public const long ExpectedId = 4242;

	public const string ExpectedSubject = "Intergroup meeting moved";

	public const string ExpectedSender = "Dave B";

	public const string ExpectedUuid = "123e4567-e89b-12d3-a456-426614174000";

	public const long ExpectedCreatedAt = 1788000000;

	/// <summary>The payload Fellowship actually sends, so tests assert on real field names.</summary>
	public static Dictionary<string, object> Payload() => new(StringComparer.Ordinal)
	{
		["id"] = ExpectedId,
		["uuid"] = ExpectedUuid,
		["subject"] = ExpectedSubject,
		["body"] = "September intergroup is now the 14th, same room.",
		["sender"] = ExpectedSender,
		["created_at"] = ExpectedCreatedAt,
		["reply_to"] = 0,
	};

	/// <summary>Seal a payload to a fresh keypair, and answer both halves.</summary>
	public static Sealed Seal(IDictionary<string, object>? payload = null)
	{
		using var rsa = RSA.Create(KeyBits);

		var sealed_ = SealTo(rsa, payload ?? Payload());

		return sealed_ with { PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() };
	}

	/// <summary>Seal to a keypair the caller already holds.</summary>
	public static Sealed SealTo(RSA rsa, IDictionary<string, object> payload)
	{
		ArgumentNullException.ThrowIfNull(rsa);

		var json = JsonSerializer.SerializeToUtf8Bytes(payload);
		var compressed = Deflate(json);

		// A fresh content key per message: GCM fails catastrophically on a
		// repeated key/nonce pair, and never keeping one is what guarantees
		// it never repeats.
		var contentKey = RandomNumberGenerator.GetBytes(ContentKeyBytes);
		var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
		var ciphertext = new byte[compressed.Length];
		var tag = new byte[TagBytes];

		using (var gcm = new AesGcm(contentKey, TagBytes))
		{
			gcm.Encrypt(nonce, compressed, ciphertext, tag);
		}

		// The envelope Fellowship packs: nonce, tag, ciphertext, base64.
		var packed = new byte[NonceBytes + TagBytes + ciphertext.Length];
		nonce.CopyTo(packed, 0);
		tag.CopyTo(packed, NonceBytes);
		ciphertext.CopyTo(packed, NonceBytes + TagBytes);

		// OaepSHA1, because that is the only OAEP PHP's
		// openssl_public_encrypt() performs. See the class remarks.
		var wrapped = rsa.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA1);

		return new Sealed
		{
			WrappedKey = Convert.ToBase64String(wrapped),
			Payload = Convert.ToBase64String(packed),
			PrivateKeyPem = string.Empty,
		};
	}

	/// <summary>A private key belonging to nobody in this test.</summary>
	public static string StrangersPrivateKey()
	{
		using var rsa = RSA.Create(KeyBits);

		return rsa.ExportPkcs8PrivateKeyPem();
	}

	/// <summary>PHP's <c>gzencode</c>: gzip, not raw deflate.</summary>
	private static byte[] Deflate(byte[] plaintext)
	{
		using var output = new MemoryStream();

		using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
		{
			gzip.Write(plaintext, 0, plaintext.Length);
		}

		return output.ToArray();
	}
}
