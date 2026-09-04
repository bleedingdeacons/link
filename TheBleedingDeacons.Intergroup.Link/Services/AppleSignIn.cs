using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Sign in with Apple, one half per head.
///
/// <para>Shaped like <see cref="PushRegistrar"/> deliberately: the shared
/// half decides what an unanswered sheet means, and each platform file
/// answers only the question its platform can. Here "unanswered" means
/// the member cancelled, which is not an error and must not be reported
/// as one.</para>
/// </summary>
public sealed partial class AppleSignIn : IAppleSignIn
{
	public bool IsAvailable => PlatformIsAvailable();

	public async Task<string?> GetIdTokenAsync(string nonce, CancellationToken cancellationToken = default)
	{
		if (!IsAvailable || string.IsNullOrEmpty(nonce))
		{
			return null;
		}

		return await PlatformGetIdTokenAsync(nonce, cancellationToken).ConfigureAwait(false);
	}

	// A method rather than a partial property: the analysers do not
	// follow a partial property's implementation across platform files
	// and report it as both unused and unassigned, which is two false
	// warnings for a syntax that buys nothing here.
	private static partial bool PlatformIsAvailable();

	private partial Task<string?> PlatformGetIdTokenAsync(string nonce, CancellationToken cancellationToken);
}
