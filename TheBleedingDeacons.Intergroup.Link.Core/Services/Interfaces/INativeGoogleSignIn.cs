namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Google sign-in through the platform's own account picker, ending in a
/// Firebase ID token for Fellowship to verify.
///
/// <para><b>Why a seam.</b> Android does it with Credential Manager and
/// iOS with GoogleSignIn, and both hand the Google credential to Firebase
/// Auth. The interface keeps <c>DeviceAuthService</c> from knowing which
/// head it is on, and lets it fall back to the browser wherever this says
/// no. Shaped like <see cref="IAppleSignIn"/> for the same reason.</para>
///
/// <para><b>Firebase is only a go-between.</b> The Firebase session is
/// signed out the moment the ID token is in hand. Fellowship mints the
/// credential this handset actually keeps, exactly as on the browser path,
/// so nothing about enrolment, revocation or the keypair changes.</para>
/// </summary>
public interface INativeGoogleSignIn
{
	/// <summary>
	/// Whether this build can try at all: a mobile head with a Firebase
	/// configuration that carries the Google OAuth client. False sends the
	/// member to the browser instead, with no error shown.
	/// </summary>
	bool IsAvailable { get; }

	Task<NativeGoogleSignInResult> SignInAsync(CancellationToken cancellationToken = default);
}

public enum NativeGoogleSignInOutcome
{
	/// <summary><see cref="NativeGoogleSignInResult.IdToken"/> holds a Firebase ID token.</summary>
	SignedIn,

	/// <summary>
	/// The picker answered "cancelled". Usually the member closing it, but
	/// not always: Android's answers it without showing anything once
	/// Google has paused the prompt. <see cref="NativeGoogleSignInResult.Detail"/>
	/// says which.
	/// </summary>
	Cancelled,

	/// <summary>
	/// This handset could not do it — no Play Services, no Google account
	/// support, a configuration Google refuses. The browser still can.
	/// </summary>
	Unavailable,
}

public sealed record NativeGoogleSignInResult(NativeGoogleSignInOutcome Outcome, string IdToken = "")
{
	public static NativeGoogleSignInResult SignedIn(string idToken) => new(NativeGoogleSignInOutcome.SignedIn, idToken);

	public static NativeGoogleSignInResult Cancelled { get; } = new(NativeGoogleSignInOutcome.Cancelled);

	public static NativeGoogleSignInResult Unavailable { get; } = new(NativeGoogleSignInOutcome.Unavailable);

	/// <summary>
	/// What the platform said, for the screen. Never a token: only the
	/// words of the error it answered instead of one.
	/// </summary>
	public string Detail { get; init; } = string.Empty;

	public NativeGoogleSignInResult Because(string detail) => this with { Detail = detail };
}
