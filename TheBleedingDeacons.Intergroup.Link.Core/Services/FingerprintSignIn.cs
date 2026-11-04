using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Signing out without letting go of the phone's place on the list, and
/// getting back in with a finger.
///
/// <para><b>What "signed out" means once this is set up.</b> Without a
/// fingerprint, signing out revokes the device row, deletes the keypair
/// and empties the history — the phone is off the list, and getting back
/// on means a full sign-in. With one, signing out <i>locks</i>: the
/// enrolment, the keypair and the history all stay, messages go on
/// arriving, and the next way in is the finger (or, when the finger is not
/// taken, the phone's own screen-lock code) rather than Google or a
/// password. The full sign-out is still there, as "Remove this phone",
/// because a phone being handed on is exactly the case a lock does not
/// cover.</para>
///
/// <para><b>Set up at the first sign-in, not offered later.</b> The moment
/// the phone has just been enrolled is the one moment the member is
/// holding it and has proved who they are, so that is when the sheet is
/// raised. A member who dismisses it can turn it on from Settings; a
/// phone with no fingerprint set up is simply never asked.</para>
///
/// <para>Plain net10.0 rather than in the app, so the rules about when a
/// lock applies are tested here with a stand-in reader. The sheet itself
/// is <see cref="IFingerprintReader"/>'s.</para>
/// </summary>
public sealed class FingerprintSignIn
{
	/// <summary>What the sheet is headed, every time it is raised.</summary>
	public const string Title = "Sign in to Link";

	/// <summary>Under the heading, when it is raised to set fingerprint sign-in up.</summary>
	public const string SetUpPrompt = "Touch the sensor to sign in with your fingerprint from now on.";

	/// <summary>Under the heading, when it is raised to get back in.</summary>
	public const string UnlockPrompt = "Touch the sensor, or use your phone's screen lock, to see your messages.";

	private readonly IFingerprintReader _reader;
	private readonly IFingerprintState _state;

	public FingerprintSignIn(IFingerprintReader reader, IFingerprintState state)
	{
		_reader = reader;
		_state = state;
	}

	/// <summary>Whether this phone can read a fingerprint at all, as of now.</summary>
	public bool IsAvailable => _reader.IsAvailable;

	/// <summary>Whether the member has set fingerprint sign-in up on this phone.</summary>
	public bool IsEnabled => _state.Enabled;

	/// <summary>
	/// Whether the member has signed out and must give a finger to get back in.
	/// </summary>
	public bool IsLocked => _state.Enabled && _state.Locked;

	/// <summary>
	/// Whether signing out can be a lock rather than a removal.
	///
	/// <para>Needs both halves now: the member set it up, <i>and</i> the
	/// phone can still confirm them — with a finger, or with its own code
	/// if the fingerprints have since been deleted. Locking a phone with
	/// neither would leave the member a screen asking for something it
	/// cannot take.</para>
	/// </summary>
	public bool CanLock => _state.Enabled && _reader.CanConfirm;

	/// <summary>
	/// Raise the sheet to set fingerprint sign-in up, and keep the answer.
	///
	/// <para>Called once a sign-in has enrolled this phone, and from the
	/// switch in Settings. Anything but a matched finger leaves it off —
	/// a member who closed the sheet has not agreed to anything.</para>
	/// </summary>
	public async Task<FingerprintResult> SetUpAsync(CancellationToken cancellationToken = default)
	{
		// Whatever happens next, this phone is in use by whoever just
		// proved who they are. A lock left over from somebody before them
		// is not theirs to inherit.
		_state.Locked = false;

		if (!_reader.IsAvailable)
		{
			_state.Enabled = false;
			Log.Information("Fingerprint sign-in not set up: this phone has no fingerprint to read");

			return FingerprintResult.Unavailable();
		}

		var result = await _reader.ConfirmAsync(Title, SetUpPrompt, cancellationToken).ConfigureAwait(false);
		_state.Enabled = result.Confirmed;

		Log.Information("Fingerprint sign-in set-up answered {FingerprintOutcome}", result.Outcome);

		return result;
	}

	/// <summary>
	/// Sign out by locking, if this phone can. Answers whether it did; a
	/// false is the caller's cue to sign out the whole way instead.
	/// </summary>
	public bool Lock()
	{
		if (!CanLock)
		{
			return false;
		}

		_state.Locked = true;
		Log.Information("Signed out with fingerprint sign-in; the enrolment stays and the next way in is a finger");

		return true;
	}

	/// <summary>
	/// Ask for the finger that gets the member back in, or the phone's own
	/// code when the finger is not taken.
	///
	/// <para>An unlocked phone answers confirmed without raising anything,
	/// so a caller that asks twice does not put the sheet up twice. A
	/// phone with no fingerprint <i>and</i> no screen lock left answers
	/// <see cref="FingerprintOutcome.Unavailable"/> and stays locked: the
	/// way on from there is a full sign-in, which is the caller's to
	/// offer.</para>
	/// </summary>
	public async Task<FingerprintResult> UnlockAsync(CancellationToken cancellationToken = default)
	{
		if (!IsLocked)
		{
			return new FingerprintResult(FingerprintOutcome.Confirmed);
		}

		if (!_reader.CanConfirm)
		{
			Log.Warning("Signed out with a fingerprint, and this phone has neither a fingerprint nor a screen lock now");

			return FingerprintResult.Unavailable(
				"This phone has no fingerprint or screen lock set up any more. Sign in another way to carry on.");
		}

		var result = await _reader.ConfirmAsync(Title, UnlockPrompt, cancellationToken).ConfigureAwait(false);

		Log.Information("Fingerprint sign-in answered {FingerprintOutcome}", result.Outcome);

		if (result.Confirmed)
		{
			_state.Locked = false;
		}

		return result;
	}

	/// <summary>
	/// Forget fingerprint sign-in altogether: turned off in Settings, or
	/// the phone has been removed and whoever signs in next starts
	/// clean.
	/// </summary>
	public void Forget()
	{
		_state.Enabled = false;
		_state.Locked = false;
	}
}
