using System.Security.Cryptography;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What the app does with an envelope it cannot open, and a payload that
/// is not shaped the way it expects.
///
/// <para><b>Every one of these is a refusal, and refusals are the half
/// that is never exercised by hand.</b> A message that opens is proved
/// the first time anybody uses the app; a message that will not open is
/// the one that reaches a member as an empty screen, and the only thing
/// standing between "returns null" and "throws in a push handler that
/// must not throw" is this file.</para>
///
/// <para><see cref="MessagePayloadCipherTests"/> proves the happy path
/// and the cross-language contract with Fellowship. This proves the
/// unhappy ones.</para>
/// </summary>
public sealed class EnvelopeRefusalTests
{
	[Theory]
	[InlineData("", "cGF5bG9hZA==", "pem")]
	[InlineData("d3JhcHBlZA==", "", "pem")]
	[InlineData("d3JhcHBlZA==", "cGF5bG9hZA==", "")]
	public void AnEnvelopeMissingAnyOfItsThreePartsWillNotOpen(string wrapped, string payload, string pem)
	{
		// Null, not an exception. This runs on the push path, where a
		// throw kills the process Android started to deliver the message.
		Assert.Null(MessagePayloadCipher.Open(wrapped, payload, pem));
	}

	[Fact]
	public void SomethingThatIsNotBase64WillNotOpen()
	{
		// A truncated or re-encoded field arrives looking like this.
		Assert.Null(MessagePayloadCipher.Open("not base64!", "also not base64!", Pem()));
	}

	[Fact]
	public void AnEnvelopeTooShortToHoldANonceAndTagWillNotOpen()
	{
		// 12 bytes of nonce plus 16 of tag is the floor; anything at or
		// under it cannot contain ciphertext, and slicing it would throw
		// rather than return.
		var tooShort = Convert.ToBase64String(new byte[28]);

		Assert.Null(MessagePayloadCipher.Open(Convert.ToBase64String(new byte[256]), tooShort, Pem()));
	}

	[Fact]
	public void AnEnvelopeSealedToAnotherHandsetWillNotOpen()
	{
		// The realistic case, and the one that matters: a handset that
		// regenerated its keypair still holds messages sealed to the old
		// public half. Nobody can re-seal them — Fellowship never had the
		// private key — so this is permanent, and it must read as "cannot
		// open" rather than as a crash.
		using var theirs = RSA.Create(2048);
		using var mine = RSA.Create(2048);

		var contentKey = RandomNumberGenerator.GetBytes(32);
		var wrapped = theirs.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA1);

		var packed = new byte[12 + 16 + 8];
		RandomNumberGenerator.Fill(packed);

		Assert.Null(MessagePayloadCipher.Open(
			Convert.ToBase64String(wrapped),
			Convert.ToBase64String(packed),
			mine.ExportPkcs8PrivateKeyPem()));
	}

	[Fact]
	public void AnEnvelopeWhoseCiphertextHasBeenTamperedWithWillNotOpen()
	{
		// AES-GCM authenticates as well as encrypts, so a flipped bit in
		// the body fails the tag rather than yielding rubbish. Asserted
		// because "decrypts to nonsense" and "refuses" are very different
		// things to show a member.
		using var key = RSA.Create(2048);

		var contentKey = RandomNumberGenerator.GetBytes(32);
		var wrapped = key.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA1);

		var packed = new byte[12 + 16 + 32];
		RandomNumberGenerator.Fill(packed);

		Assert.Null(MessagePayloadCipher.Open(
			Convert.ToBase64String(wrapped),
			Convert.ToBase64String(packed),
			key.ExportPkcs8PrivateKeyPem()));
	}

	[Fact]
	public void APrivateKeyThatIsNotAKeyWillNotOpen()
	{
		Assert.Null(MessagePayloadCipher.Open(
			Convert.ToBase64String(new byte[256]),
			Convert.ToBase64String(new byte[64]),
			"-----BEGIN PRIVATE KEY-----\nnonsense\n-----END PRIVATE KEY-----"));
	}

	// ── The payload, once it is open ──────────────────────────────────

	[Fact]
	public void AMessageWithNoUsableIdIsRefused()
	{
		// Everything downstream keys on the id: the history dedupes on it,
		// the poll pages from it, the notification is tagged with it. A
		// message with id 0 would collide with every other one.
		Assert.Null(LinkMessage.FromPayload(Payload("""{"subject":"hello"}""")));
		Assert.Null(LinkMessage.FromPayload(Payload("""{"id":0,"subject":"hello"}""")));
	}

	[Fact]
	public void AnIdSentAsAStringIsStillAnId()
	{
		// Every FCM data value is a string, so a payload arriving by push
		// can carry numbers that way. Refusing them would mean pushed
		// messages silently never storing.
		var message = LinkMessage.FromPayload(Payload("""{"id":"42","created_at":"1800000000"}"""));

		Assert.NotNull(message);
		Assert.Equal(42, message.Id);
		Assert.Equal(1800000000, message.CreatedAt);
	}

	[Fact]
	public void AbsentFieldsBecomeEmptyRatherThanNull()
	{
		// The UI binds these directly; a null would render as nothing in
		// some places and throw in others.
		var message = LinkMessage.FromPayload(Payload("""{"id":7}"""));

		Assert.NotNull(message);
		Assert.Equal(string.Empty, message.Subject);
		Assert.Equal(string.Empty, message.Body);
		Assert.Equal(string.Empty, message.Sender);
		Assert.Equal(string.Empty, message.Uuid);
	}

	[Fact]
	public void AFieldOfTheWrongTypeIsTreatedAsAbsent()
	{
		// An object or an array where a string was expected is a server
		// that has changed shape. Empty is survivable; throwing on the
		// push path is not.
		var message = LinkMessage.FromPayload(Payload("""{"id":7,"subject":{"nested":true},"sender":[1,2]}"""));

		Assert.NotNull(message);
		Assert.Equal(string.Empty, message.Subject);
		Assert.Equal(string.Empty, message.Sender);
	}

	[Fact]
	public void AnUnreadMessageHasNoReadTimestampRatherThanZero()
	{
		// IsRead reads ReadAt, so a stored 0 would be indistinguishable
		// from "read at the epoch" if it were not normalised to null.
		var unread = LinkMessage.FromPayload(Payload("""{"id":7,"read_at":0}"""));
		var read = LinkMessage.FromPayload(Payload("""{"id":7,"read_at":1800000000}"""));

		Assert.NotNull(unread);
		Assert.Null(unread.ReadAt);
		Assert.False(unread.IsRead);

		Assert.NotNull(read);
		Assert.True(read.IsRead);
	}

	[Fact]
	public void TheSentTimeIsReadAsUnixSeconds()
	{
		var message = LinkMessage.FromPayload(Payload("""{"id":7,"created_at":1800000000}"""));

		Assert.NotNull(message);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), message.Sent);
	}

	private static Dictionary<string, JsonElement> Payload(string json) =>
		JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

	private static string Pem()
	{
		using var key = RSA.Create(2048);
		return key.ExportPkcs8PrivateKeyPem();
	}
}
