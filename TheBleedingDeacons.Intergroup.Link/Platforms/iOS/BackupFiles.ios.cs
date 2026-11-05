namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half.
///
/// <para><b>The picker offers any data file.</b> Declaring a type of our own
/// for <c>.linkbackup</c> would mean exporting a UTI in Info.plist for the sake
/// of a filter, and a file that is not a backup simply will not open.</para>
/// </summary>
public sealed partial class BackupFiles
{
	private static partial void PlatformBeforeSharing()
	{
		// Nothing to do: the sheet shares the file from where it is.
	}

	private static partial FilePickerFileType PlatformFileTypes() =>
		new(new Dictionary<DevicePlatform, IEnumerable<string>>
		{
			[DevicePlatform.iOS] = ["public.data"],
		});
}
