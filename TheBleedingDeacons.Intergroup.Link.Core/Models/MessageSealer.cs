using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Seals a message to one device, so that only that device can open it.
///
/// <para>On Firebase the sender's phone does the sealing, because the server
/// must never see a message. The format is the one Fellowship's PHP sealer
/// writes and <see cref="MessagePayloadCipher"/> opens: AES-256-GCM over gzip,
/// with a fresh content key per envelope wrapped by RSA-OAEP. See that class for
/// why each detail is what it is, SHA-1 above all.</para>
///
/// <para>The tests' own copy in <c>Sealing</c> stays separate on purpose. It is
/// what checks the opening side against PHP, and sharing this code with it would
/// leave a change here nothing to disagree with.</para>
/// </summary>
public static class MessageSealer
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int ContentKeyBytes = 32;

	/// <summary>
	/// Seal a payload to a device's public key.
	/// </summary>
	/// <param name="payload">The fields <see cref="LinkMessage.FromPayload"/> reads.</param>
	/// <param name="publicKey">Base64 SubjectPublicKeyInfo, as the device enrolled it.</param>
	/// <exception cref="FormatException">The key is not base64.</exception>
	/// <exception cref="CryptographicException">The key is not an RSA public key.</exception>
	public static SealedEnvelope Seal(IReadOnlyDictionary<string, object> payload, string publicKey)
	{
		ArgumentNullException.ThrowIfNull(payload);
		ArgumentException.ThrowIfNullOrEmpty(publicKey);

		using var rsa = RSA.Create();
		rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);

		var compressed = Compress(JsonSerializer.SerializeToUtf8Bytes(payload));
		var contentKey = RandomNumberGenerator.GetBytes(ContentKeyBytes);

		try
		{
			var packed = new byte[NonceBytes + TagBytes + compressed.Length];
			var nonce = packed.AsSpan(0, NonceBytes);
			RandomNumberGenerator.Fill(nonce);

			using (var gcm = new AesGcm(contentKey, TagBytes))
			{
				gcm.Encrypt(
					nonce,
					compressed,
					packed.AsSpan(NonceBytes + TagBytes),
					packed.AsSpan(NonceBytes, TagBytes));
			}

			return new SealedEnvelope(
				Convert.ToBase64String(rsa.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA1)),
				Convert.ToBase64String(packed));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(contentKey);
		}
	}

	private static byte[] Compress(byte[] plaintext)
	{
		using var output = new MemoryStream();

		using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
		{
			gzip.Write(plaintext, 0, plaintext.Length);
		}

		return output.ToArray();
	}
}

/// <summary>What one device receives: the wrapped key and the sealed payload.</summary>
/// <param name="WrappedKey">The <c>k</c> field.</param>
/// <param name="Payload">The <c>p</c> field.</param>
public sealed record SealedEnvelope(string WrappedKey, string Payload);
