using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Support;

/// <summary>
/// A scripted Fellowship.
///
/// <para>Deliberately a recording double rather than a mock with
/// expectations: a scenario says "the intergroup was told this handset
/// cannot read its messages", and that reads better as a question asked
/// of a list afterwards than as an expectation set up before.</para>
///
/// <para>Each route answers whatever the scenario set on it and records
/// what it was asked, so a feature can drive the handset through outcomes
/// a real server would need a revoked device or a tunnel to produce.</para>
/// </summary>
public sealed class FakeFellowshipClient : IFellowshipClient
{
	/// <summary>
	/// The messages Fellowship holds, in the clear, as it actually holds
	/// them.
	///
	/// <para><b>Plaintext, and sealed only when a handset asks.</b> That
	/// is what the server does — <c>MessageController::inbox</c> calls
	/// <c>seal(payloadFor($message), $device-&gt;publicKey)</c> on every
	/// fetch, from a body stored as plain TEXT. A double that handed back
	/// envelopes sealed once, when the scenario wrote them, would model a
	/// store-sealed server this repository does not talk to — and it did,
	/// which is how a feature file came to claim that replacing a key
	/// loses every message sent before it. It does not.</para>
	/// </summary>
	public List<ServerMessage> Stored { get; } = [];

	/// <summary>
	/// The public half Fellowship holds for this device, and therefore the
	/// one it seals to.
	///
	/// <para>Separate from whatever the handset is carrying, because the
	/// two genuinely do come apart: a keystore entry is invalidated and
	/// the server knows nothing about it until the handset presents a
	/// replacement. Rotation is the moment they are put back in step.
	/// </para>
	/// </summary>
	public string DevicePublicKey { get; set; } = string.Empty;

	/// <summary>How many of this member's messages are unread, per the server.</summary>
	public int Unread { get; set; }

	/// <summary>A refusal to answer with instead of a page, or null.</summary>
	public InboxPage? Refusal { get; set; }

	/// <summary>
	/// Message ids to corrupt on the way out, one byte of ciphertext each.
	/// GCM authenticates, so these are payloads that will not open rather
	/// than ones that open to nonsense.
	///
	/// <para>Per message rather than per page, because the scenario that
	/// matters is one bad envelope among good ones.</para>
	/// </summary>
	public HashSet<long> Tampered { get; } = [];

	public SignInStart? SignIn { get; set; } =
		new() { State = "state-1", AuthorizationUrl = "https://aa-bristol.org/oauth/start" };

	public EnrolmentResult Enrolment { get; set; } =
		EnrolmentResult.Ok(new DeviceSession { Token = "device-token", MemberId = 7, MemberName = "Dave B" });

	public PasswordSetResult PasswordSet { get; set; } = PasswordSetResult.Ok();

	public bool PasswordLinkAccepted { get; set; } = true;

	public SendResult Send { get; set; } = new() { MessageId = 900, Recipients = 1 };

	public FellowshipDirectory Directory { get; set; } = FellowshipDirectory.Empty;

	public bool MarkReadAccepted { get; set; } = true;

	/// <summary>Every <c>sinceId</c> the handset has polled with, in order.</summary>
	public List<long> PolledSince { get; } = [];

	/// <summary>Every message id reported read to the server, in order.</summary>
	public List<long> MarkedRead { get; } = [];

	public List<EnrolmentRequest> Enrolments { get; } = [];

	public List<SendRequest> Sent { get; } = [];

	public List<string> RotatedKeys { get; } = [];

	public int KeyFaultReports { get; private set; }

	public int SignOuts { get; private set; }

	public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken) =>
		Task.FromResult(SignIn);

	public Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken)
	{
		Enrolments.Add(request);

		return Task.FromResult(Enrolment);
	}

	public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken) =>
		Task.FromResult(PasswordLinkAccepted);

	public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken) =>
		Task.FromResult(PasswordSet);

	/// <summary>
	/// Answers what is waiting above <paramref name="sinceId"/>, and not
	/// what is waiting at or below it.
	///
	/// <para><b>The filter is here because the server has one.</b>
	/// Fellowship's query is <c>message_id &gt; %d</c>, strictly
	/// exclusive. A double that handed back its whole page whatever it was
	/// asked would let a scenario prove things no real handset can do —
	/// most obviously that a message already held comes back carrying a
	/// read flag set on another device, which it does not.</para>
	/// </summary>
	public Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken)
	{
		PolledSince.Add(sinceId);

		if (Refusal is not null)
		{
			return Task.FromResult(Refusal);
		}

		var page = Stored
			.Where(message => message.Id > sinceId)
			.Select(message => Sealing.Seal(message.Id, message.Payload, DevicePublicKey))
			.Select(envelope => Tampered.Contains(envelope.Id) ? Sealing.Tamper(envelope) : envelope)
			.ToList();

		return Task.FromResult(new InboxPage { Messages = page, Unread = Unread });
	}

	public Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken)
	{
		MarkedRead.Add(messageId);

		return Task.FromResult(MarkReadAccepted);
	}

	public Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken)
	{
		Sent.Add(request);

		return Task.FromResult(Send);
	}

	public Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken) =>
		Task.FromResult(Directory);

	public Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken) =>
		Task.FromResult(true);

	public Task<RotateKeyResult> RotateKeyAsync(
		string token, RotateKeyRequest request, CancellationToken cancellationToken)
	{
		RotatedKeys.Add(request.PublicKey);

		// A credential is required since Fellowship 1.7, and the double
		// enforces it so a scenario cannot prove a rotation the server
		// would refuse.
		var proven = request.Code.Length > 0
			|| request.IdToken.Length > 0
			|| (request.Email.Length > 0 && request.Password.Length > 0);

		return Task.FromResult(proven
			? RotateKeyResult.Ok()
			: RotateKeyResult.Failed("Sign in to replace this key."));
	}

	public Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken)
	{
		KeyFaultReports++;

		return Task.FromResult(true);
	}

	public Task<bool> SignOutAsync(string token, CancellationToken cancellationToken)
	{
		SignOuts++;

		return Task.FromResult(true);
	}
}

/// <summary>
/// This handset's keypair, held in a field instead of a keychain.
///
/// <para>The real one is <c>SecureStorage</c> and therefore MAUI, which
/// is the whole reason <see cref="IDeviceKeyStore"/> exists. The keys
/// themselves are real RSA-2048 — a scenario about a message that will
/// not open has to be able to seal one that genuinely will not.</para>
///
/// <para><b>Losing the key here loses it only here.</b> When a platform
/// invalidates a keystore entry the handset stops being able to read its
/// private half, and Fellowship goes on holding the public half it was
/// given and goes on sealing to it, knowing nothing. That asymmetry lives
/// in <see cref="FakeFellowshipClient.DevicePublicKey"/>, which is the
/// server's copy and is untouched by anything here — so a scenario can
/// lose a key and still have messages arrive that genuinely cannot be
/// opened.</para>
/// </summary>
public sealed class FakeDeviceKeyStore : IDeviceKeyStore
{
	private Sealing.Keypair? _keys = Sealing.NewKeypair();

	public int Regenerations { get; private set; }

	public Task<bool> HasKeyAsync() => Task.FromResult(_keys is not null);

	/// <summary>
	/// A new keypair, which is also how a scenario reproduces the fault
	/// this app is most careful about: everything sealed to the old half
	/// is now unopenable, here and on the server both.
	/// </summary>
	public Task<string> RegenerateAsync()
	{
		Regenerations++;
		_keys = Sealing.NewKeypair();

		return Task.FromResult(_keys.PublicKey);
	}

	public Task<string> PublicKeyAsync() => Task.FromResult(_keys?.PublicKey ?? string.Empty);

	public Task<string> PrivateKeyAsync() => Task.FromResult(_keys?.PrivateKeyPem ?? string.Empty);

	/// <summary>
	/// A factory reset, a restored backup, a cleared keystore. Reads back
	/// empty rather than throwing, which is what the real one does.
	/// </summary>
	public Task ClearAsync()
	{
		_keys = null;

		return Task.CompletedTask;
	}
}

/// <summary>
/// One message as Fellowship holds it: an id, and the payload in the
/// clear.
/// </summary>
public sealed record ServerMessage(long Id, Dictionary<string, object> Payload);

/// <summary>The device token, held in memory rather than in a keychain.</summary>
public sealed class FakeSessionStore : ISessionStore
{
	public DeviceSession? Session { get; set; } = new() { Token = "device-token", MemberId = 7, MemberName = "Dave B" };

	public int Clears { get; private set; }

	public Task<DeviceSession?> LoadAsync() => Task.FromResult(Session);

	public Task SaveAsync(DeviceSession session)
	{
		Session = session;

		return Task.CompletedTask;
	}

	public Task ClearAsync()
	{
		Clears++;
		Session = null;

		return Task.CompletedTask;
	}
}
