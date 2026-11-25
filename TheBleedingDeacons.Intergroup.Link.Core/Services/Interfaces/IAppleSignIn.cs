namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Raises the platform's own Sign in with Apple sheet.
///
/// <para><b>Why this is a seam rather than a call.</b> Apple's sheet is
/// iOS-only, and the system hands the app a signed ID token directly. The
/// interface exists to keep
/// <see cref="IFellowshipClient"/>'s callers from having to know which
/// head they are on.</para>
///
/// <para><b>The nonce goes through untouched.</b> It arrives already
/// hashed — <c>FirebaseFellowshipClient.StartSignInAsync</c> gives Apple
/// the hash and keeps the raw value for Firebase — and Apple copies what
/// it is given into the token's <c>nonce</c> claim verbatim. Hashing it
/// again here would make every token fail verification for a reason
/// neither side could see.</para>
/// </summary>
public interface IAppleSignIn
{
	/// <summary>
	/// Whether this build can raise the sheet at all.
	///
	/// <para>False on Android, and false on an iOS installation whose
	/// signature lacks the <c>com.apple.developer.applesignin</c>
	/// entitlement. Only a paid Apple Developer Program team can grant it,
	/// so an app signed by a free personal team never has it. The button
	/// is hidden rather than shown-and-broken.</para>
	/// </summary>
	bool IsAvailable { get; }

	/// <summary>
	/// The ID token from Apple, or null if the member cancelled.
	/// </summary>
	/// <param name="nonce">Fellowship's nonce, passed through unmodified.</param>
	Task<string?> GetIdTokenAsync(string nonce, CancellationToken cancellationToken = default);
}
