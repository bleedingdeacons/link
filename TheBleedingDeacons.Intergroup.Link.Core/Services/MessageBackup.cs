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
/// <para><b>Restoring always needs a sign-in.</b> A backup holds no credential
/// and no key, so it gets nobody in. It only adds to a phone a member has
/// already signed in to, and only if the backup is theirs.</para>
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

	public const string NotWritten =
		"This backup could not be saved to this phone. Nothing on this phone has changed.";

	private readonly ISessionStore _sessions;
	private readonly IMessageHistory _history;
	private readonly IThemeChoice _theme;
	private readonly IArrivalSound _sound;
	private readonly IRecipientListing _listing;
	private readonly IPullToRefresh _pull;

	public MessageBackup(
		ISessionStore sessions,
		IMessageHistory history,
		IThemeChoice theme,
		IArrivalSound sound,
		IRecipientListing listing,
		IPullToRefresh pull)
	{
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

		var contents = new BackupContents
		{
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
	/// is this member's. Anything else changes nothing.
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

		if (contents.MemberId != session.MemberId)
		{
			// Who it belonged to stays out of the log: it is not this
			// member's business, and nobody needs it to diagnose this.
			Log.Warning("A backup from another member was refused on the phone of member {MemberId}", session.MemberId);

			return BackupRestore.Refused(BackupRestoreOutcome.OtherMember, OtherMember);
		}

		var merge = await _history.MergeAsync(session.MemberId, contents.History, cancellationToken).ConfigureAwait(false);

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

				return new BackupRestore(BackupRestoreOutcome.Restored, Describe(merge))
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
