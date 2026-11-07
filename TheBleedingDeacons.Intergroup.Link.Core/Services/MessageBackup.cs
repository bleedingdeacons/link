using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Making a backup and restoring one: what goes in, and what has to be true
/// before anything comes back out.
///
/// <para><b>What the member is told about a backup, because each of these is
/// easy to get wrong.</b> It is a copy of their messages outside Link. Clearing
/// messages, signing out and Remove this phone do not touch it. The intergroup
/// cannot delete it either, so an erasure request cannot reach it. Settings
/// says all of this before a file is made, and the wording is in
/// <see cref="OutsideLink"/> so it can only be said one way.</para>
///
/// <para><b>Restoring always needs a sign-in.</b> A backup holds no credential,
/// so it gets nobody in. It holds the phone's private key, which opens messages
/// and proves nothing.</para>
///
/// <para><b>Two ways to restore.</b> From Settings, onto a phone already signed
/// in, it adds the messages and nothing else. From the sign-in screen it is held
/// until the member signs in, and that sign-in takes the old phone's place and
/// cuts it off. If the old phone's place can't be taken, this phone enrols as new
/// and gets the messages anyway. See <see cref="HoldForSignInAsync"/>.</para>
///
/// <para>Plain net10.0 rather than in the app, so these rules are tested here.
/// Where the bytes go is the app's: the share sheet and the file picker.</para>
/// </summary>
public sealed class MessageBackup
{
	/// <summary>What a backup is, in the words Settings uses.</summary>
	public const string OutsideLink =
		"A backup is a copy of your messages outside Link. Clearing messages, signing out and Remove this phone "
		+ "do not touch it. The intergroup cannot delete it either, so an erasure request cannot reach it. "
		+ "It stays until you delete it yourself.";

	/// <summary>What else is in a backup, and what restoring it elsewhere does.</summary>
	public const string HoldsTheKey =
		"It also holds this phone's key, which opens your messages, so choose a passphrase nobody could guess. "
		+ "Restoring it from the sign-in screen of another phone signs this phone out.";

	/// <summary>What a forgotten passphrase costs.</summary>
	public const string ForgottenPassphrase =
		"If you forget the passphrase, this backup cannot be opened, and nobody can recover it.";

	/// <summary>
	/// The one message for a wrong passphrase and a damaged file, because
	/// they cannot be told apart. See <see cref="BackupFile"/>.
	/// </summary>
	public const string CouldNotOpen =
		"This backup could not be opened. The passphrase may be wrong, the file may be damaged, or it may have "
		+ "been made by a newer version of Link. Nothing on this phone has changed.";

	public const string OtherMember =
		"This backup belongs to another member, so it has not been restored. Nothing on this phone has changed.";

	public const string NotSignedIn = "Sign in to Link before restoring a backup.";

	/// <summary>A backup is open and waiting for the sign-in that will move the phone here.</summary>
	public const string WillTakeOver =
		"Backup opened. Sign in below to finish. This phone will take over from the phone this backup came from, "
		+ "and that phone will be signed out.";

	/// <summary>The same, for a backup with no device in it.</summary>
	public const string WillJoinAsNew =
		"Backup opened. Sign in below to finish. Your messages will come back, and this phone will be added as a "
		+ "new device.";

	/// <summary>Why a restore from the sign-in screen left the old phone alone.</summary>
	public const string CouldNotTakeOver =
		"This phone could not take over from the phone this backup came from, so it has been added as a new device. "
		+ "Anything sent to that phone since the backup was made won't reach this one.";

	public const string TookOver =
		"This phone has taken over from the phone this backup came from, which is now signed out.";

	public const string JoinedAsNew = "This phone has been added as a new device.";

	/// <summary>Restoring from Settings adds messages and leaves every phone where it is.</summary>
	public const string OtherPhoneStays =
		"The phone this backup came from stays signed in. To take over from it instead, restore from the sign-in "
		+ "screen.";

	public const string TookOverButNotWritten =
		"This phone has taken over from the phone this backup came from, but the messages in the backup could not "
		+ "be saved. Please restore it again from Settings.";

	public const string NotWritten =
		"This backup could not be saved to this phone. Nothing on this phone has changed.";

	private readonly ISessionStore _sessions;
	private readonly IMessageHistory _history;
	private readonly IThemeChoice _theme;
	private readonly IArrivalSound _sound;
	private readonly IRecipientListing _listing;
	private readonly IPullToRefresh _pull;
	private readonly IDeviceKeyStore _keys;

	/// <summary>A backup opened on the sign-in screen, holding a private key until the sign-in ends.</summary>
	private BackupContents? _held;

	public MessageBackup(
		ISessionStore sessions,
		IMessageHistory history,
		IThemeChoice theme,
		IArrivalSound sound,
		IRecipientListing listing,
		IPullToRefresh pull,
		IDeviceKeyStore keys)
	{
		_keys = keys ?? throw new ArgumentNullException(nameof(keys));
		_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
		_history = history ?? throw new ArgumentNullException(nameof(history));
		_theme = theme ?? throw new ArgumentNullException(nameof(theme));
		_sound = sound ?? throw new ArgumentNullException(nameof(sound));
		_listing = listing ?? throw new ArgumentNullException(nameof(listing));
		_pull = pull ?? throw new ArgumentNullException(nameof(pull));
	}

	/// <summary>
	/// Make a backup of what this phone holds, or answer null when nobody is
	/// signed in or the store is not theirs.
	/// </summary>
	public async Task<BackupMade?> MakeAsync(string passphrase, CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return null;
		}

		var history = await _history.SnapshotAsync(session.MemberId, cancellationToken).ConfigureAwait(false);
		if (history is null)
		{
			return null;
		}

		var at = DateTimeOffset.UtcNow;
		var privateKey = await _keys.PrivateKeyAsync().ConfigureAwait(false);

		var contents = new BackupContents
		{
			Device = session.FirebaseDeviceId.Length > 0 && privateKey.Length > 0
				? new BackupDevice { Id = session.FirebaseDeviceId, PrivateKey = privateKey }
				: null,
			MemberId = session.MemberId,
			CreatedAt = at.ToUnixTimeSeconds(),
			History = history,
			Settings = new BackupSettings
			{
				Style = (int)_theme.Style,
				Sound = _sound.Enabled,
				OnlyRegistered = _listing.OnlyRegistered,
				PullToRefresh = _pull.Enabled,
			},
		};

		// Off the caller's thread: the key derivation is slow on purpose, and
		// the caller is usually a button.
		var file = await Task.Run(() => BackupFile.Seal(contents, passphrase), cancellationToken).ConfigureAwait(false);

		Log.Information(
			"Made a backup of {Messages} message(s) and {Sent} sent cop(ies) for member {MemberId}",
			history.Messages.Count,
			history.Sent.Count,
			session.MemberId);

		return new BackupMade(BackupFile.FileNameFor(at.ToLocalTime()), file, session.MemberId, at);
	}

	/// <summary>
	/// Restore a backup into this phone's history, if it can be opened and
	/// is this member's. Anything else changes nothing. Every phone stays where
	/// it is: taking a phone's place is <see cref="HoldForSignInAsync"/>.
	///
	/// <para>The settings it carries come back in the answer rather than
	/// being applied here. Setting a style repaints the screen, which has to
	/// happen on the main thread, and this has been off it since the key
	/// derivation. See <see cref="ApplySettings"/>.</para>
	/// </summary>
	public async Task<BackupRestore> RestoreAsync(byte[] file, string passphrase, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(file);

		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return BackupRestore.Refused(BackupRestoreOutcome.NotSignedIn, NotSignedIn);
		}

		var contents = await Task.Run(() => BackupFile.Open(file, passphrase), cancellationToken).ConfigureAwait(false);
		if (contents?.History is null)
		{
			Log.Information("A backup could not be opened");

			return BackupRestore.Refused(BackupRestoreOutcome.CouldNotOpen, CouldNotOpen);
		}

		var fromAnotherPhone = contents.Device is { } device && device.Id != session.FirebaseDeviceId;

		return await TakeInAsync(contents, session, fromAnotherPhone ? OtherPhoneStays : null, null, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Open a backup on the sign-in screen and keep it for the sign-in that comes
	/// next. Nothing on the phone changes until then, and a wrong passphrase can be
	/// tried again.
	/// </summary>
	public async Task<BackupRestore> HoldForSignInAsync(byte[] file, string passphrase, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(file);

		var contents = await Task.Run(() => BackupFile.Open(file, passphrase), cancellationToken).ConfigureAwait(false);
		if (contents?.History is null)
		{
			Log.Information("A backup could not be opened");

			return BackupRestore.Refused(BackupRestoreOutcome.CouldNotOpen, CouldNotOpen);
		}

		_held = contents;

		return new BackupRestore(BackupRestoreOutcome.Held, contents.Device is null ? WillJoinAsNew : WillTakeOver);
	}

	/// <summary>Whether a backup is waiting for a sign-in.</summary>
	public bool IsHolding => _held is not null;

	/// <summary>Let go of a held backup, and the private key in it.</summary>
	public void ForgetHeld() => _held = null;

	/// <summary>
	/// Point an enrolment at the held backup's device, if there is one to take over
	/// and its key can be read.
	/// </summary>
	public EnrolmentRequest AimedAtHeldDevice(EnrolmentRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (_held?.Device is not { } device)
		{
			return request;
		}

		var publicKey = device.PublicKey();

		return publicKey.Length == 0
			? request
			: request with { TakeOverDeviceId = device.Id, TakeOverPublicKey = publicKey };
	}

	/// <summary>
	/// A takeover has worked, so keep the backup's key before the session is saved.
	/// If this phone can't keep it, the sign-in stops here and can be tried again: the
	/// device is already this phone's, and taking it over a second time does no harm.
	/// </summary>
	public async Task KeepHeldKeyAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_held?.Device is { } device)
		{
			await _keys.ImportAsync(device.PrivateKey).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// The sign-in has finished, so take in the held backup and let it go. Answers
	/// null when none was held.
	/// </summary>
	/// <param name="tookOver">Whether the sign-in took the backup's device over.</param>
	/// <param name="cancellationToken">Stops waiting for the history.</param>
	public async Task<BackupRestore?> FinishHeldAsync(bool tookOver, CancellationToken cancellationToken = default)
	{
		if (_held is not { } contents)
		{
			return null;
		}

		_held = null;

		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			return BackupRestore.Refused(BackupRestoreOutcome.NotSignedIn, NotSignedIn);
		}

		string opening;
		if (tookOver)
		{
			opening = TookOver;
		}
		else
		{
			opening = contents.Device is null ? JoinedAsNew : CouldNotTakeOver;
		}

		var restored = await TakeInAsync(contents, session, null, opening, cancellationToken).ConfigureAwait(false);

		return tookOver && restored.Outcome == BackupRestoreOutcome.NotWritten
			? BackupRestore.Refused(BackupRestoreOutcome.NotWritten, TookOverButNotWritten)
			: restored;
	}

	/// <summary>Join an opened backup to this phone's history, if it is this member's.</summary>
	/// <param name="contents">The opened backup.</param>
	/// <param name="session">Who is signed in.</param>
	/// <param name="after">A sentence for the end of the message about what was restored.</param>
	/// <param name="before">A sentence for the start of it.</param>
	/// <param name="cancellationToken">Stops waiting for the history.</param>
	private async Task<BackupRestore> TakeInAsync(
		BackupContents contents,
		DeviceSession session,
		string? after,
		string? before,
		CancellationToken cancellationToken)
	{
		if (contents.MemberId != session.MemberId)
		{
			// Who it belonged to stays out of the log: it is not this
			// member's business, and nobody needs it to diagnose this.
			Log.Warning("A backup from another member was refused on the phone of member {MemberId}", session.MemberId);

			return BackupRestore.Refused(BackupRestoreOutcome.OtherMember, OtherMember);
		}

		var merge = await _history.MergeAsync(session.MemberId, contents.History!, cancellationToken).ConfigureAwait(false);

		switch (merge.Outcome)
		{
			case HistoryMergeOutcome.OtherMember:
				Log.Warning("This phone's history is not held for member {MemberId}, so a backup was refused", session.MemberId);

				return BackupRestore.Refused(BackupRestoreOutcome.OtherMember, OtherMember);

			case HistoryMergeOutcome.NotWritten:
				Log.Warning("A restored backup could not be written to this phone's history");

				return BackupRestore.Refused(BackupRestoreOutcome.NotWritten, NotWritten);

			default:
				Log.Information(
					"Restored a backup for member {MemberId}: {Added} added, {StayedGone} stayed gone",
					session.MemberId,
					merge.Added,
					merge.StayedGone);

				return new BackupRestore(BackupRestoreOutcome.Restored, string.Join(' ', new[] { before, Describe(merge), after }.Where(s => s is not null)))
				{
					Added = merge.Added,
					StayedGone = merge.StayedGone,
					Settings = contents.Settings,
					MadeAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, contents.CreatedAt)),
				};
		}
	}

	/// <summary>
	/// Put a restored backup's choices back. Call it on the main thread: see
	/// <see cref="RestoreAsync"/>.
	///
	/// <para>A style this build does not know, from a later version, leaves
	/// the current one alone rather than drawing the app in nothing.</para>
	/// </summary>
	public void ApplySettings(BackupSettings? settings)
	{
		if (settings is null)
		{
			return;
		}

		if (settings.Style is { } style && Enum.IsDefined((LinkStyle)style))
		{
			_theme.Style = (LinkStyle)style;
		}

		if (settings.Sound is { } sound)
		{
			_sound.Enabled = sound;
		}

		if (settings.OnlyRegistered is { } onlyRegistered)
		{
			_listing.OnlyRegistered = onlyRegistered;
		}

		if (settings.PullToRefresh is { } pull)
		{
			_pull.Enabled = pull;
		}
	}

	private static string Describe(HistoryMerge merge)
	{
		var added = merge.Added switch
		{
			0 => "Your backup is restored. It held no messages that were not already on this phone.",
			1 => "Your backup is restored. 1 message was added to this phone.",
			_ => $"Your backup is restored. {merge.Added} messages were added to this phone.",
		};

		return merge.StayedGone == 0
			? added
			: added + " Anything you deleted or cleared stays gone, on this phone and in what was restored.";
	}
}

/// <summary>A backup that has just been made, ready to hand to the share sheet.</summary>
/// <param name="FileName">What to call it.</param>
/// <param name="File">The sealed bytes.</param>
/// <param name="MemberId">Whose messages it holds.</param>
/// <param name="At">When it was made.</param>
#pragma warning disable CA1819 // The sealed file is bytes, handed once to the share sheet.
public sealed record BackupMade(string FileName, byte[] File, long MemberId, DateTimeOffset At);
#pragma warning restore CA1819

/// <summary>How a restore ended.</summary>
public enum BackupRestoreOutcome
{
	Restored,

	/// <summary>Opened on the sign-in screen, and waiting for the sign-in.</summary>
	Held,

	/// <summary>A wrong passphrase, or a damaged file. They cannot be told apart.</summary>
	CouldNotOpen,

	OtherMember,

	NotSignedIn,

	NotWritten,
}

/// <summary>What a restore did, and the sentence that says so.</summary>
public sealed record BackupRestore(BackupRestoreOutcome Outcome, string Message)
{
	public bool Succeeded => Outcome == BackupRestoreOutcome.Restored;

	public int Added { get; init; }

	public int StayedGone { get; init; }

	/// <summary>The choices the backup carried. Not yet applied: see <see cref="MessageBackup.ApplySettings"/>.</summary>
	public BackupSettings? Settings { get; init; }

	/// <summary>When the restored backup was made.</summary>
	public DateTimeOffset? MadeAt { get; init; }

	internal static BackupRestore Refused(BackupRestoreOutcome outcome, string message) => new(outcome, message);
}
