using Android.App;
using Android.Content.PM;
using Android.OS;

namespace TheBleedingDeacons.Intergroup.Link;

[Activity(
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	LaunchMode = LaunchMode.SingleTop,
	ConfigurationChanges = ConfigChanges.ScreenSize
		| ConfigChanges.Orientation
		| ConfigChanges.UiMode
		| ConfigChanges.ScreenLayout
		| ConfigChanges.SmallestScreenSize
		| ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
	/// <summary>
	/// Ask for notification permission on Android 13 and later.
	///
	/// <para>Asked here rather than on the sign-in screen because a
	/// permission prompt in the middle of an OAuth flow is a prompt people
	/// dismiss to get on with what they were doing. A member who says no
	/// still has a working app — the message list fills on every poll and
	/// on every launch — they just do not get told about a message until
	/// they open it.</para>
	/// </summary>
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		if (OperatingSystem.IsAndroidVersionAtLeast(33)
			&& CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
		{
			RequestPermissions([Android.Manifest.Permission.PostNotifications], 1);
		}
	}
}
