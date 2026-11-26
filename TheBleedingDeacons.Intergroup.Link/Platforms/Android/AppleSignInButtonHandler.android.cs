using Microsoft.Maui.Handlers;

namespace TheBleedingDeacons.Intergroup.Link.Views;

/// <summary>
/// The Android half: an empty view.
///
/// <para>Sign in with Apple is never offered on Android (see
/// <c>AppleSignIn.android.cs</c>), so the button is always hidden here. MAUI
/// still builds a platform view for every element in the page, visible or
/// not, and an element with no handler for its platform throws when the
/// page loads. So this is the smallest handler that satisfies that.</para>
/// </summary>
public sealed partial class AppleSignInButtonHandler : ViewHandler<AppleSignInButton, Android.Views.View>
{
	protected override Android.Views.View CreatePlatformView() => new(Context);
}
