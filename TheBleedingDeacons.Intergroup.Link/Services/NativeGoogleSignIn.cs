using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Google sign-in through the platform's own account picker, ending in a
/// Firebase ID token. See <see cref="INativeGoogleSignIn"/>.
///
/// <para>Shaped like <see cref="AppleSignIn"/>: the shared half says what
/// an unavailable build answers, and each platform file answers only the
/// question its platform can.</para>
/// </summary>
public sealed partial class NativeGoogleSignIn : INativeGoogleSignIn
{
	public bool IsAvailable => PlatformIsAvailable();

	public async Task<NativeGoogleSignInResult> SignInAsync(CancellationToken cancellationToken = default)
	{
		if (!IsAvailable)
		{
			return NativeGoogleSignInResult.Unavailable;
		}

		return await PlatformSignInAsync(cancellationToken).ConfigureAwait(false);
	}

	// A method rather than a partial property, as in AppleSignIn: the
	// analysers do not follow a partial property across platform files.
	private static partial bool PlatformIsAvailable();

	private static partial Task<NativeGoogleSignInResult> PlatformSignInAsync(CancellationToken cancellationToken);
}
