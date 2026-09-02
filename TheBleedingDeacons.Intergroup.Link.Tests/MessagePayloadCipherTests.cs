using System.Security.Cryptography;
using TheBleedingDeacons.Intergroup.Link.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The wire contract with Fellowship.
///
/// <para><b>This is the most load-bearing test in the app.</b> Everything
/// else here can be checked by reading it; this cannot. The envelope is
/// built by PHP and opened by C#, and the two ends agree only if OAEP's
/// hash, the nonce length, the tag position, the gzip and the base64 all
/// match. Get one wrong and the failure is a message that arrives on
/// somebody's phone and silently will not open — which nobody would
/// connect to a line of code.</para>
///
/// <para>The envelope here is built by <see cref="Sealing"/>, a
/// transcription of Fellowship's sealer; the other half of the contract
/// is Fellowship's own <c>MessageSealerTest</c>, which opens an envelope
/// in PHP the way this class opens one in C#. Each side's test does the
/// other side's job, so drift on either shows up as a red test on the
/// opposite one.</para>
/// </summary>
public sealed class MessagePayloadCipherTests
{
	[Fact]
	public void ItOpensWhatFellowshipSeals()
	{
		var sealed_ = Sealing.Seal();

		var opened = MessagePayloadCipher.Open(sealed_.WrappedKey, sealed_.Payload, sealed_.PrivateKeyPem);

		Assert.NotNull(opened);
		Assert.Equal(Sealing.ExpectedSubject, opened["subject"].GetString());
		Assert.Equal(Sealing.ExpectedSender, opened["sender"].GetString());
		Assert.Equal(Sealing.ExpectedId, opened["id"].GetInt64());
		Assert.Contains("14th", opened["body"].GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public void AnOpenedEnvelopeBecomesAMessage()
	{
		var sealed_ = Sealing.Seal();

		var opened = MessagePayloadCipher.Open(sealed_.WrappedKey, sealed_.Payload, sealed_.PrivateKeyPem);
		Assert.NotNull(opened);

		var message = LinkMessage.FromPayload(opened);

		Assert.NotNull(message);
		Assert.Equal(Sealing.ExpectedId, message.Id);
		Assert.Equal(Sealing.ExpectedUuid, message.Uuid);
		Assert.Equal(Sealing.ExpectedSubject, message.Subject);
		Assert.Equal(Sealing.ExpectedCreatedAt, message.CreatedAt);
		Assert.False(message.IsRead);
	}

	[Fact]
	public void AnotherHandsetsKeyDoesNotOpenIt()
	{
		// The whole point of the scheme: an envelope is readable by the
		// device it was sealed to and by nothing else — the server
		// included, which never held the private half.
		var sealed_ = Sealing.Seal();

		Assert.Null(MessagePayloadCipher.Open(
			sealed_.WrappedKey,
			sealed_.Payload,
			Sealing.StrangersPrivateKey()));
	}

	[Fact]
	public void EachSealUsesAFreshContentKey()
	{
		// GCM fails catastrophically on a repeated key/nonce pair, and a
		// fresh content key every time is what guarantees it never
		// happens. Two seals of identical input must share nothing.
		using var rsa = RSA.Create(Sealing.KeyBits);

		var first = Sealing.SealTo(rsa, Sealing.Payload());
		var second = Sealing.SealTo(rsa, Sealing.Payload());

		Assert.NotEqual(first.WrappedKey, second.WrappedKey);
		Assert.NotEqual(first.Payload, second.Payload);

		var pem = rsa.ExportPkcs8PrivateKeyPem();

		Assert.NotNull(MessagePayloadCipher.Open(first.WrappedKey, first.Payload, pem));
		Assert.NotNull(MessagePayloadCipher.Open(second.WrappedKey, second.Payload, pem));
	}

	[Fact]
	public void ATamperedPayloadDoesNotOpen()
	{
		// GCM authenticates, so an altered payload fails to open rather
		// than decrypting to something plausible.
		var sealed_ = Sealing.Seal();

		var bytes = Convert.FromBase64String(sealed_.Payload);
		bytes[^1] ^= 0xFF;

		Assert.Null(MessagePayloadCipher.Open(
			sealed_.WrappedKey,
			Convert.ToBase64String(bytes),
			sealed_.PrivateKeyPem));
	}

	[Fact]
	public void ATamperedWrappedKeyDoesNotOpen()
	{
		var sealed_ = Sealing.Seal();

		var bytes = Convert.FromBase64String(sealed_.WrappedKey);
		bytes[^1] ^= 0xFF;

		Assert.Null(MessagePayloadCipher.Open(
			Convert.ToBase64String(bytes),
			sealed_.Payload,
			sealed_.PrivateKeyPem));
	}

	[Theory]
	[InlineData("", "p", "key")]
	[InlineData("k", "", "key")]
	[InlineData("k", "p", "")]
	[InlineData("not base64!", "also not!", "nor this")]
	public void RubbishInAnswersNull(string wrappedKey, string payload, string privateKey)
	{
		// Null covers every reason at once because the caller does the same
		// thing about all of them — see the remarks on MessagePayloadCipher.
		Assert.Null(MessagePayloadCipher.Open(wrappedKey, payload, privateKey));
	}

	[Fact]
	public void ATruncatedPayloadDoesNotThrow()
	{
		// Shorter than the nonce and tag it is supposed to start with. A
		// push handler is not a place to throw from.
		var sealed_ = Sealing.Seal();

		Assert.Null(MessagePayloadCipher.Open(
			sealed_.WrappedKey,
			Convert.ToBase64String([1, 2, 3, 4]),
			sealed_.PrivateKeyPem));
	}

	[Fact]
	public void TheWorstCasePayloadFitsInsideAnFcmDataMessage()
	{
		// FCM caps a data message at 4096 bytes, and the wrapped key alone
		// is 344 base64 characters of it. Fellowship's MessageRequest caps
		// are what make this a known quantity; if either moves, this test
		// and its opposite number in PHP both say so.
		//
		// Built from random hex rather than prose, because prose compresses
		// hard and would prove nothing about the ceiling.
		var payload = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["id"] = 999999,
			["uuid"] = Sealing.ExpectedUuid,
			["subject"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(100)),
			["body"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(1000)),
			["sender"] = new string('x', 200),
			["created_at"] = 1893456000,
			["reply_to"] = 999999,
			["read_at"] = 1893456000,
		};

		var sealed_ = Sealing.Seal(payload);

		// Both data keys travel too, and count against the same budget.
		var onTheWire = "k".Length + sealed_.WrappedKey.Length + "p".Length + sealed_.Payload.Length;

		Assert.True(
			onTheWire < 4096,
			"The largest message the API accepts must still fit in an FCM data message.");

		Assert.NotNull(MessagePayloadCipher.Open(sealed_.WrappedKey, sealed_.Payload, sealed_.PrivateKeyPem));
	}
}
