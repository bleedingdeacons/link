namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Where a backup's bytes go and come from: the phone's share sheet and file
/// picker, and the date of the last one.
///
/// <para>In the app rather than in Link.Core, unlike the other seams, because
/// nothing in Core needs to stand in for it. What a backup is and when it may
/// be restored is <see cref="MessageBackup"/>'s, and that takes bytes.</para>
/// </summary>
public interface IBackupFiles
{
	/// <summary>
	/// Hand a sealed backup to the share sheet. Answers false if the sheet
	/// could not be raised, and never throws.
	///
	/// <para>True does not mean the member saved it. Neither platform says
	/// what happened after the sheet opened, and on Android this returns as
	/// soon as it has.</para>
	/// </summary>
	Task<bool> ShareAsync(string fileName, byte[] file, CancellationToken cancellationToken = default);

	/// <summary>
	/// Let the member choose a backup, and read it. Answers null when they
	/// closed the picker, and empty bytes when the file could not be read,
	/// which <see cref="Models.BackupFile"/> treats like any other damaged
	/// file. Never throws.
	/// </summary>
	Task<byte[]?> PickAsync(CancellationToken cancellationToken = default);

	/// <summary>When this member last made or restored a backup on this phone, or null.</summary>
	DateTimeOffset? LastBackup(long memberId);

	/// <summary>Remember a backup's date. A date older than the one held is ignored.</summary>
	void RememberBackup(long memberId, DateTimeOffset at);
}
