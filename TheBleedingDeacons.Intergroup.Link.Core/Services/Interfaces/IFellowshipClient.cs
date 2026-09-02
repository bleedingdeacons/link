using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Everything Link asks of the Fellowship plugin.
///
/// <para>One interface for the whole REST surface rather than one per
/// route, because there is exactly one implementation and exactly one
/// server; splitting it would be ceremony. What it is for is the test
/// seam — every view model takes this, so none of them needs a network.
/// </para>
/// </summary>
public interface IFellowshipClient
{
	/// <summary>
	/// Begin a sign-in. Answers an authorization URL for Google, or a
	/// nonce for Apple, plus the state that ties the rest of the flow to
	/// this attempt.
	/// </summary>
	Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default);

	/// <summary>
	/// Finish enrolment: send the public key and the credential, receive
	/// the device token.
	/// </summary>
	Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Messages newer than the id this handset already holds, still
	/// sealed. Opening them is the caller's job — see
	/// <see cref="MessagePayloadCipher"/> — because only the caller has
	/// the private key.
	/// </summary>
	Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default);

	Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Send a message. Recipients are member ids or a committee slug,
	/// never addresses — see <see cref="DirectoryMember"/>.
	/// </summary>
	Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default);

	Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default);

	/// <summary>Tell the server this handset's current FCM registration token.</summary>
	Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default);

	/// <summary>
	/// Present a new public key after the platform invalidated the old
	/// keypair.
	///
	/// <para>Messages already sealed to the old key stay unreadable, and
	/// here that is a fact rather than a policy: the server never held the
	/// private half, so it could not re-seal them even if asked.</para>
	/// </summary>
	Task<bool> RotateKeyAsync(string token, string publicKey, CancellationToken cancellationToken = default);

	/// <summary>
	/// Report that this handset cannot open its messages.
	///
	/// <para>Has to be reported rather than inferred: from the server a
	/// handset with a lost private key looks perfectly healthy right up
	/// until a message it cannot read.</para>
	/// </summary>
	Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default);

	/// <summary>Sign out, which revokes this device server-side.</summary>
	Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>What <c>/auth/device/start</c> answers.</summary>
public sealed record SignInStart
{
	public required string State { get; init; }

	/// <summary>Set for the server-side flow (Google); empty for Apple.</summary>
	public string AuthorizationUrl { get; init; } = string.Empty;

	/// <summary>Set for the client-side flow (Apple); empty for Google.</summary>
	public string Nonce { get; init; } = string.Empty;

	public bool IsBrowserFlow => !string.IsNullOrEmpty(AuthorizationUrl);
}

/// <summary>What the app posts to <c>/auth/device/exchange</c>.</summary>
public sealed record EnrolmentRequest
{
	/// <summary>The one-time code the browser carried back (Google).</summary>
	public string Code { get; init; } = string.Empty;

	/// <summary>The state issued at start (Apple).</summary>
	public string State { get; init; } = string.Empty;

	/// <summary>The platform-issued ID token (Apple).</summary>
	public string IdToken { get; init; } = string.Empty;

	/// <summary>Base64 SubjectPublicKeyInfo for this handset's new keypair.</summary>
	public required string PublicKey { get; init; }

	/// <summary>Something a member will recognise in the Devices list.</summary>
	public string Label { get; init; } = string.Empty;

	public required string Platform { get; init; }

	public string PushProvider { get; init; } = string.Empty;

	public string PushToken { get; init; } = string.Empty;
}

/// <summary>
/// The outcome of an enrolment attempt.
///
/// <para>A failure carries the server's own message where there is one.
/// "That address does not match a member record" is the single most
/// common thing to go wrong here and the only one the member can act on,
/// so it must reach the screen rather than being flattened into
/// "sign-in failed".</para>
/// </summary>
public sealed record EnrolmentResult
{
	public DeviceSession? Session { get; init; }

	public string Error { get; init; } = string.Empty;

	public bool Succeeded => Session is not null;

	public static EnrolmentResult Failed(string error) => new() { Error = error };

	public static EnrolmentResult Ok(DeviceSession session) => new() { Session = session };
}

/// <summary>One sealed envelope, exactly as the server sends it.</summary>
public sealed record SealedMessage
{
	public required long Id { get; init; }

	/// <summary>The <c>k</c> field: the content key, wrapped to this device.</summary>
	public required string WrappedKey { get; init; }

	/// <summary>
	/// The <c>p</c> field: nonce, tag, ciphertext, base64.
	///
	/// <para>The id beside these is not part of the envelope — it is what
	/// lets a handset page its poll without opening anything.</para>
	/// </summary>
	public required string Payload { get; init; }
}

/// <summary>One page of the inbox.</summary>
public sealed record InboxPage
{
	public IReadOnlyList<SealedMessage> Messages { get; init; } = [];

	/// <summary>How many of this member's messages are unread, per the server.</summary>
	public int Unread { get; init; }

	/// <summary>
	/// False when the fetch did not happen — offline, a 401, a 500.
	///
	/// <para>Distinct from an empty page, which is the ordinary answer to
	/// "anything new?" and must not be mistaken for a failure. A caller
	/// that conflated them would clear its unread badge every time the
	/// network dropped.</para>
	/// </summary>
	public bool Succeeded { get; init; } = true;

	public static InboxPage Failed { get; } = new() { Succeeded = false };
}

/// <summary>What the app posts to send a message.</summary>
public sealed record SendRequest
{
	public required string Subject { get; init; }

	public required string Body { get; init; }

	/// <summary>Opaque Unity member ids from the directory. Never addresses.</summary>
	public IReadOnlyList<long> MemberIds { get; init; } = [];

	/// <summary>A committee slug, when the site allows committee sends from the app.</summary>
	public string Committee { get; init; } = string.Empty;

	/// <summary>The message being replied to, or 0.</summary>
	public long ReplyToId { get; init; }
}

/// <summary>The outcome of a send.</summary>
public sealed record SendResult
{
	public long MessageId { get; init; }

	public int Recipients { get; init; }

	public string Error { get; init; } = string.Empty;

	public bool Succeeded => MessageId > 0;

	public static SendResult Failed(string error) => new() { Error = error };
}
