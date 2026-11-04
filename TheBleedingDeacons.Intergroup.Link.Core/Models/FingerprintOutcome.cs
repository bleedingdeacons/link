namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// How the platform's fingerprint sheet came back.
///
/// <para>Four answers rather than a bool, because each one asks the
/// screen for something different. A finger that matched gets the member
/// in; a sheet they closed says nothing, as a closed browser tab says
/// nothing; a sheet the phone refused to go on with (too many wrong
/// fingers) needs telling; and a phone with no fingerprint to read is not
/// a failure at all but a reason to sign in another way.</para>
/// </summary>
public enum FingerprintOutcome
{
	/// <summary>A finger the phone holds was read.</summary>
	Confirmed,

	/// <summary>The member closed the sheet, or something else on the phone took it away.</summary>
	Cancelled,

	/// <summary>The phone stopped asking — usually too many fingers it did not know.</summary>
	Failed,

	/// <summary>No sensor, or no fingerprint set up on the phone, or no window to show the sheet in.</summary>
	Unavailable,
}

/// <summary>
/// An outcome, and what the phone said with it.
///
/// <para>The detail is the platform's own sentence — "Too many attempts.
/// Try again later." — and is what reaches the screen when the outcome is
/// <see cref="FingerprintOutcome.Failed"/>. Empty the rest of the time.</para>
/// </summary>
public sealed record FingerprintResult(FingerprintOutcome Outcome, string Detail = "")
{
	public bool Confirmed => Outcome == FingerprintOutcome.Confirmed;

	public static FingerprintResult Unavailable(string detail = "") => new(FingerprintOutcome.Unavailable, detail);
}
