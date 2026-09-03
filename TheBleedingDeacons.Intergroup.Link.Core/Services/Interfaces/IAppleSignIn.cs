namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Raises the platform's own Sign in with Apple sheet.
///
/// <para><b>Why this is a seam rather than a call.</b> Apple's sheet is
/// iOS-only and, unlike Google's, has no browser leg — the system hands
/// the app a signed ID token directly. That makes it the one credential
/// path with no shared implementation, so the interface exists to keep
/// <see cref="IFellowshipClient"/>'s callers from having to know which
/// head they are on.</para>
///
/// <para><b>The nonce goes through untouched.</b> Fellowship issues it at
/// <c>/auth/device/start</c>, stores it against the state, and compares
/// the token's <c>nonce</c> claim to what it stored. Apple copies the
/// value it is given into that claim verbatim, so hashing it here — a
/// common convention, and what Firebase's flow asks for — would make
/// every token fail verification for a reason neither side could see.
/// </para>
/// </summary>
public interface IAppleSignIn
{
	/// <summary>
	/// Whether this build can raise the sheet at all.
	///
	/// <para>False on Android, and false on an iOS build without the
	/// <c>com.apple.developer.applesignin</c> entitlement — which is most
	/// of them, because that entitlement needs a paid Apple Developer
	/// Program team and a free personal team cannot sign an app that asks
	/// for it. The button is hidden rather than shown-and-broken.</para>
	/// </summary>
	bool IsAvailable { get; }

	/// <summary>
	/// The ID token from Apple, or null if the member cancelled.
	/// </summary>
	/// <param name="nonce">Fellowship's nonce, passed through unmodified.</param>
	Task<string?> GetIdTokenAsync(string nonce, CancellationToken cancellationToken = default);
}
