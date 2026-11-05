using System.Globalization;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The share sheet and the file picker. See <see cref="IBackupFiles"/>.
///
/// <para>Partial, with one half per head, as <see cref="FingerprintReader"/>
/// is. The heads differ in what a picker can be told to show, and in where a
/// shared file is put. The shared half owns the rule both obey: nothing here
/// throws, because a sheet that will not open must not take Settings with
/// it.</para>
///
/// <para><b>The file stays in the app's cache until the next backup.</b>
/// Android's share sheet returns before whichever app the member chose has
/// read it, so deleting it straight away would hand that app nothing. What
/// stays is only the sealed bytes, in a folder this app alone can read.</para>
///
/// <para>The date is in <see cref="Preferences"/>, as the other small things
/// about this screen are, and is kept per member. A new member on a handed-on
/// phone has made no backup, and the same member signing back in still has the
/// one they made.</para>
/// </summary>
public sealed partial class BackupFiles : IBackupFiles
{
	private const string CacheFolder = "backups";
	private const string MimeType = "application/octet-stream";

	public async Task<bool> ShareAsync(string fileName, byte[] file, CancellationToken cancellationToken = default)
	{
		try
		{
			var folder = Path.Combine(FileSystem.CacheDirectory, CacheFolder);

			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, recursive: true);
			}

			Directory.CreateDirectory(folder);

			var path = Path.Combine(folder, fileName);

			await File.WriteAllBytesAsync(path, file, cancellationToken).ConfigureAwait(false);

			PlatformBeforeSharing();

			await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
			{
				Title = "Save your Link backup",
				File = new ShareFile(path, MimeType),
			})).ConfigureAwait(false);

			return true;
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "A backup could not be handed to the share sheet");

			return false;
		}
	}

	public async Task<byte[]?> PickAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var picked = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions
			{
				PickerTitle = "Choose your Link backup",
				FileTypes = PlatformFileTypes(),
			})).ConfigureAwait(false);

			if (picked is null)
			{
				return null;
			}

			var source = await picked.OpenReadAsync().ConfigureAwait(false);

			await using (source.ConfigureAwait(false))
			{
				return await ReadLimitedAsync(source, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			return null;
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "A chosen backup could not be read");

			return [];
		}
	}

	public DateTimeOffset? LastBackup(long memberId)
	{
		var stored = Preferences.Default.Get(PreferenceKey(memberId), 0L);

		return stored > 0 ? DateTimeOffset.FromUnixTimeSeconds(stored) : null;
	}

	public void RememberBackup(long memberId, DateTimeOffset at)
	{
		if (LastBackup(memberId) is not { } held || held < at)
		{
			Preferences.Default.Set(PreferenceKey(memberId), at.ToUnixTimeSeconds());
		}
	}

	private static string PreferenceKey(long memberId) =>
		"last_backup_" + memberId.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// Read no more than <see cref="BackupFile"/> will look at, plus the one
	/// byte that tells it the file is too large. A member can point the picker
	/// at any file on their phone, and this must not try to hold a film in
	/// memory.
	/// </summary>
	private static async Task<byte[]> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
	{
		using var output = new MemoryStream();

		var buffer = new byte[81920];
		int read;

		while (output.Length <= BackupFile.MaximumFileBytes
			&& (read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
		{
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
		}

		return output.ToArray();
	}

	/// <summary>Anything this head needs doing before the sheet is raised.</summary>
	private static partial void PlatformBeforeSharing();

	/// <summary>What the picker offers on this head.</summary>
	private static partial FilePickerFileType PlatformFileTypes();
}
