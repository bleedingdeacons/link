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
	/// <summary>The next page the inbox will answer.</summary>
	public InboxPage Inbox { get; set; } = new();

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

		return Task.FromResult(
			Inbox.Succeeded
				? Inbox with { Messages = [.. Inbox.Messages.Where(m => m.Id > sinceId)] }
				: Inbox);
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

	public Task<bool> RotateKeyAsync(string token, string publicKey, CancellationToken cancellationToken)
	{
		RotatedKeys.Add(publicKey);

		return Task.FromResult(true);
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
/// <para><b>Losing the key and losing the pair are different things,
/// and the difference is the whole feature.</b> When a platform
/// invalidates a keystore entry, the handset stops being able to read
/// its private half; Fellowship goes on holding the public half it was
/// given and goes on sealing to it, knowing nothing. So
/// <see cref="ClearAsync"/> takes the pair away from the handset and
/// leaves <see cref="SealingKey"/> where it was — which is what lets a
/// scenario seal a message the handset genuinely cannot open, rather
/// than one nobody could have sent.</para>
/// </summary>
public sealed class FakeDeviceKeyStore : IDeviceKeyStore
{
	private Sealing.Keypair _keys = Sealing.NewKeypair();
	private bool _lost;

	public int Regenerations { get; private set; }

	/// <summary>
	/// The public half Fellowship holds, and therefore the one a scenario
	/// seals to. Survives the handset losing its own copy.
	/// </summary>
	public string SealingKey => _keys.PublicKey;

	public Task<bool> HasKeyAsync() => Task.FromResult(!_lost);

	/// <summary>
	/// A new keypair, which is also how a scenario reproduces the fault
	/// this app is most careful about: everything sealed to the old half
	/// is now unopenable, here and on the server both.
	/// </summary>
	public Task<string> RegenerateAsync()
	{
		Regenerations++;
		_keys = Sealing.NewKeypair();
		_lost = false;

		return Task.FromResult(_keys.PublicKey);
	}

	public Task<string> PublicKeyAsync() => Task.FromResult(_lost ? string.Empty : _keys.PublicKey);

	public Task<string> PrivateKeyAsync() => Task.FromResult(_lost ? string.Empty : _keys.PrivateKeyPem);

	/// <summary>
	/// A factory reset, a restored backup, a cleared keystore. Reads back
	/// empty rather than throwing, which is what the real one does.
	/// </summary>
	public Task ClearAsync()
	{
		_lost = true;

		return Task.CompletedTask;
	}
}

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
