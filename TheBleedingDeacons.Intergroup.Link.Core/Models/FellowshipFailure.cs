namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Why a request to Fellowship did not succeed.
///
/// <para><b>The distinction this exists for is "try again" against "stop
/// trying".</b> A bare bool collapsed a phone in a tunnel, a 500 and a
/// revoked device into one answer, and the app rendered all three as
/// <c>Offline</c> — so a handset an administrator had deliberately cut
/// off looked identical to one briefly out of signal, kept polling, and
/// told its member nothing. Hand carries the same taxonomy for the same
/// reason.</para>
///
/// <para><b>Only <see cref="Unauthenticated"/> and
/// <see cref="NotEligible"/> ever sign a handset out.</b> Everything else
/// is ordinary and the next sync tries again. Getting that backwards
/// gives you an app that signs itself out in a car park, which is a worse
/// fault than the one this fixes.</para>
/// </summary>
public enum FellowshipFailure
{
	/// <summary>Nothing went wrong, or nothing was attempted.</summary>
	None = 0,

	/// <summary>Could not reach the server at all. Worth retrying.</summary>
	Network,

	/// <summary>
	/// The token is gone — revoked by an administrator, revoked by a
	/// sign-out elsewhere, or invalidated wholesale by a WordPress salt
	/// rotation. The app must drop it and show sign-in.
	/// </summary>
	Unauthenticated,

	/// <summary>
	/// The token was recognised and the person behind it refused: the
	/// address no longer matches a member record Fellowship will talk to.
	/// Worth saying plainly, because it is the one a member can act on.
	/// </summary>
	NotEligible,

	/// <summary>Anything else the server said no to. Ordinary; retry.</summary>
	Server,
}
