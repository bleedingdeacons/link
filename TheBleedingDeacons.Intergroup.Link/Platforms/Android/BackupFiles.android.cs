namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Android half.
///
/// <para><b>The picker offers everything.</b> <c>.linkbackup</c> has no MIME
/// type Android knows, and what a document provider calls one depends on the
/// provider: Drive and Files do not agree. A filter narrower than this would
/// hide the member's own backup from them, and anything else they choose
/// simply will not open. Android's document picker needs no storage permission
/// for any of this.</para>
///
/// <para><b>The share goes through the app's internal cache.</b> Left to its
/// default, MAUI copies a shared file to external storage first, where on the
/// older Androids this app supports another app with the storage permission
/// could read it. It is sealed, but it has no reason to be there.</para>
/// </summary>
public sealed partial class BackupFiles
{
	private static partial void PlatformBeforeSharing() =>
		FileProvider.TemporaryLocation = FileProviderLocation.Internal;

	private static partial FilePickerFileType PlatformFileTypes() =>
		new(new Dictionary<DevicePlatform, IEnumerable<string>>
		{
			[DevicePlatform.Android] = ["*/*"],
		});
}
