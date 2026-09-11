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

		KeepContentClearOfTheSystemBars();
	}

	/// <summary>
	/// Pad the window out of the way of the system navigation bar.
	///
	/// <para><b>The tab bar was sitting underneath it.</b> Its container ran
	/// the full height of the screen — to 2400 on a Pixel 6a, with the
	/// system bar owning 2274 upwards — so the bottom of the strip was
	/// behind the gesture pill, or behind three buttons, depending on the
	/// handset. On the message list, where the tabs are, that is the only
	/// navigation there is.</para>
	///
	/// <para><b>Android draws edge to edge and will not stop.</b> From API
	/// 35 an app targeting 35 or later gets the whole window and is
	/// expected to inset its own content; from 36 the opt-out
	/// (<c>windowOptOutEdgeToEdgeEnforcement</c>) is ignored as well, and
	/// the handsets this is tested on report API 37. So there is nothing to
	/// switch off — the padding has to be applied, and this is where.</para>
	///
	/// <para><b>Bottom only, and from the bars rather than the cutout.</b>
	/// MAUI already handles the status bar at the top; padding that again
	/// would push every page down by the height of the clock. What it does
	/// not handle is the bottom, which is where Shell puts the tab bar.
	/// <c>SystemBars()</c> covers both the gesture pill and three-button
	/// navigation, so this does not have to ask which the member uses.</para>
	///
	/// <para>Applied through a listener rather than once at startup because
	/// the inset changes while the app runs: rotating the handset moves the
	/// navigation bar to the side, and a keyboard appearing changes it
	/// again.</para>
	/// </summary>
	private void KeepContentClearOfTheSystemBars()
	{
		var root = FindViewById(global::Android.Resource.Id.Content);
		if (root is null)
		{
			return;
		}

		AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(
			root,
			new InsetListener());
	}

	private sealed class InsetListener : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
	{
		public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(
			global::Android.Views.View view,
			AndroidX.Core.View.WindowInsetsCompat insets)
		{
			ArgumentNullException.ThrowIfNull(view);
			ArgumentNullException.ThrowIfNull(insets);

			var bars = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());

			view.SetPadding(view.PaddingLeft, view.PaddingTop, view.PaddingRight, bars.Bottom);

			// Returned unconsumed: something below may still want to know
			// where the bars are, and swallowing the insets here would be a
			// second, quieter version of the bug this fixes.
			return insets;
		}
	}
}
