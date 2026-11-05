namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What is inside a backup, once it has been opened.
///
/// <para><b>Nothing here signs in, and nothing here may ever.</b> No device
/// token, no refresh token, no private key. A copied phone must never hold a
/// working credential, which is the reason <c>android:allowBackup</c> is false
/// in the manifest, and restoring always needs a sign-in. A test holds the
/// property names of this file to a fixed list, so adding one means changing
/// that list on purpose.</para>
/// </summary>
public sealed record BackupContents
{
	public int FormatVersion { get; init; } = BackupFile.FormatVersion;

	/// <summary>The member whose messages these are.</summary>
	public long MemberId { get; init; }

	/// <summary>When the backup was made, in Unix seconds.</summary>
	public long CreatedAt { get; init; }

	public HistorySnapshot? History { get; init; }

	public BackupSettings? Settings { get; init; }
}

/// <summary>
/// The choices about the screen that travel with a backup.
///
/// <para><b>Fingerprint sign-in does not.</b> It is a sign-in, and only a
/// finger this phone has just matched turns it on. Carrying it would switch a
/// lock on for a phone that has never read the member's finger.</para>
///
/// <para>Each is nullable so that a value this build does not know, or one a
/// later version stops writing, leaves the member's current choice alone.</para>
/// </summary>
public sealed record BackupSettings
{
	/// <summary>
	/// The <see cref="LinkStyle"/>, as the number <c>ThemeChoice</c> keeps it
	/// as, so that the JSON needs no converter built at runtime for the enum.
	/// That is the one thing here an iOS ahead-of-time build could trip over.
	/// </summary>
	public int? Style { get; init; }

	public bool? Sound { get; init; }

	public bool? OnlyRegistered { get; init; }

	public bool? PullToRefresh { get; init; }
}
