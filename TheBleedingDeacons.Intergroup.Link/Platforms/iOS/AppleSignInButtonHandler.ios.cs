using AuthenticationServices;
using Microsoft.Maui.Handlers;

namespace TheBleedingDeacons.Intergroup.Link.Views;

/// <summary>
/// The iOS half: <c>ASAuthorizationAppleIDButton</c>, the button Apple's
/// guidelines require for Sign in with Apple.
///
/// <para><b>"Continue", not "Sign in".</b> Link's other providers say
/// "Continue with Google", and Apple offers the same wording.</para>
///
/// <para><b>Black on a light page, white on a dark one</b>, as Apple's
/// guidelines ask. The style is fixed when the button is made, because the
/// system button cannot change it afterwards. A page built after the phone
/// or Link's theme changes gets the new one.</para>
///
/// <para>The corner radius follows the app's own buttons
/// (<c>ButtonCornerRadius</c>, set by ThemeChoice), so it sits with the
/// Google button above it rather than against it.</para>
/// </summary>
public sealed partial class AppleSignInButtonHandler : ViewHandler<AppleSignInButton, ASAuthorizationAppleIdButton>
{
	protected override ASAuthorizationAppleIdButton CreatePlatformView()
	{
		var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

		var button = new ASAuthorizationAppleIdButton(
			ASAuthorizationAppleIdButtonType.Continue,
			dark ? ASAuthorizationAppleIdButtonStyle.White : ASAuthorizationAppleIdButtonStyle.Black);

		if (Application.Current?.Resources.TryGetValue("ButtonCornerRadius", out var radius) == true && radius is int corner)
		{
			button.CornerRadius = corner;
		}

		return button;
	}

	protected override void ConnectHandler(ASAuthorizationAppleIdButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.TouchUpInside += OnTouchUpInside;
	}

	protected override void DisconnectHandler(ASAuthorizationAppleIdButton platformView)
	{
		platformView.TouchUpInside -= OnTouchUpInside;
		base.DisconnectHandler(platformView);
	}

	private void OnTouchUpInside(object? sender, EventArgs e) => VirtualView?.SendTapped();
}
