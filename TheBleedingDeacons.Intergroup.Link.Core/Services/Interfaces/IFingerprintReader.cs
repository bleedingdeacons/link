using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The phone's own fingerprint sheet: Android's <c>BiometricPrompt</c>,
/// iOS's <c>LAContext</c>.
///
/// <para>Behind an interface for the reason every platform seam here is:
/// the implementation is MAUI and cannot be referenced from Link.Core, and
/// the rules about when to ask and what an answer means can only be
/// tested if this can be stood in for.</para>
///
/// <para><b>It confirms who is holding the phone and proves nothing to
/// anybody else.</b> No key is unlocked by it and nothing is sent to the
/// intergroup; the device token stays where it always was. What it guards
/// is this app's screens, against somebody who picks up a phone a member
/// has signed out of.</para>
/// </summary>
public interface IFingerprintReader
{
	/// <summary>
	/// Whether the phone can read a fingerprint now: it has a sensor, and
	/// its owner has set up at least one finger.
	///
	/// <para>Asked fresh every time, not remembered. A member can remove
	/// every fingerprint from the phone's settings while Link is signed
	/// out, and a lock that waited for a finger the phone no longer reads
	/// would be a lock with no key.</para>
	/// </summary>
	bool IsAvailable { get; }

	/// <summary>
	/// Whether the sheet can confirm the holder at all: a fingerprint, or
	/// failing that the phone's own screen-lock code (PIN, pattern,
	/// password or passcode).
	///
	/// <para>Wider than <see cref="IsAvailable"/> on purpose. Fingerprint
	/// sign-in is only set up on a phone that reads a finger, but a member
	/// whose finger is not taken today — a cut, a wet hand, fingerprints
	/// deleted since — still gets back in with the code they unlock the
	/// phone with.</para>
	/// </summary>
	bool CanConfirm { get; }

	/// <summary>
	/// Raise the sheet and wait for a finger, or for the phone's code
	/// instead.
	///
	/// <para>Never throws. A sheet that could not be shown answers
	/// <see cref="FingerprintOutcome.Unavailable"/>, which is what it
	/// amounts to for the member.</para>
	/// </summary>
	Task<FingerprintResult> ConfirmAsync(string title, string subtitle, CancellationToken cancellationToken = default);
}
