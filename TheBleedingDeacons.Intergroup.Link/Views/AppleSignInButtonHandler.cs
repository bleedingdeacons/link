namespace TheBleedingDeacons.Intergroup.Link.Views;

/// <summary>
/// The handler for <see cref="AppleSignInButton"/>, one half per head:
/// <c>Platforms/iOS</c> draws Apple's system button, <c>Platforms/Android</c>
/// an empty view. Registered in <c>MauiProgram</c>.
/// </summary>
public sealed partial class AppleSignInButtonHandler
{
	public static readonly IPropertyMapper<AppleSignInButton, AppleSignInButtonHandler> Mapper =
		new PropertyMapper<AppleSignInButton, AppleSignInButtonHandler>(ViewMapper);

	public AppleSignInButtonHandler()
		: base(Mapper)
	{
	}
}
