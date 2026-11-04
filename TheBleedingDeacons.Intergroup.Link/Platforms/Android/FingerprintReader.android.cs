using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Android half: AndroidX's <c>BiometricPrompt</c>.
///
/// <para><b>A strong biometric, falling back to the phone's own code.</b>
/// Class 3 is what Android lets unlock keystore keys, which is the bar a
/// sign-in should meet; the weaker class takes some face unlocks a
/// photograph can pass. The sheet's other button is "Use PIN" (or
/// pattern, or password — whichever the phone is locked with), so a
/// finger the phone will not take today is not the end of the road.</para>
///
/// <para><b>Weak plus code below API 30</b>, because Android cannot offer
/// a strong biometric and the code together on 28 and 29, and AndroidX
/// refuses the combination there. On those phones the code is the
/// fallback either way, so the weaker class costs nothing the code did
/// not already allow.</para>
///
/// <para>The AndroidX prompt rather than the framework's, because it
/// draws the same sheet from API 24 — this app's floor — where the
/// framework one only arrived at 28.</para>
/// </summary>
public sealed partial class FingerprintReader
{
	private const int Strong = BiometricManager.Authenticators.BiometricStrong;

	/// <summary>A finger, or the phone's code. See the class remarks for the split at API 30.</summary>
	private static int FingerOrCode =>
		(OperatingSystem.IsAndroidVersionAtLeast(30) ? Strong : BiometricManager.Authenticators.BiometricWeak)
			| BiometricManager.Authenticators.DeviceCredential;

	private static partial bool PlatformIsAvailable() =>
		BiometricManager.From(Platform.AppContext).CanAuthenticate(Strong) == BiometricManager.BiometricSuccess;

	private static partial bool PlatformCanConfirm() =>
		BiometricManager.From(Platform.AppContext).CanAuthenticate(FingerOrCode) == BiometricManager.BiometricSuccess;

	private static partial async Task<FingerprintResult> PlatformConfirmAsync(
		string title,
		string subtitle,
		CancellationToken cancellationToken)
	{
		// MauiAppCompatActivity is an AppCompatActivity, which is a
		// FragmentActivity — the host the prompt draws its sheet in.
		if (Platform.CurrentActivity is not FragmentActivity activity)
		{
			return FingerprintResult.Unavailable("There is no window to show the fingerprint sheet in.");
		}

		var answered = new TaskCompletionSource<FingerprintResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var callback = new Answer(answered);
		using var prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity), callback);
		using var info = new BiometricPrompt.PromptInfo.Builder()
			.SetTitle(title)
			.SetSubtitle(subtitle)
			// No negative button: with the code allowed, that button is the
			// phone's own "Use PIN", and AndroidX throws if one is set as
			// well. Back still cancels.
			.SetAllowedAuthenticators(FingerOrCode)
			.SetConfirmationRequired(false)
			.Build();

		using var cancelled = cancellationToken.Register(() =>
			MainThread.BeginInvokeOnMainThread(prompt.CancelAuthentication));

		prompt.Authenticate(info);

		return await answered.Task.ConfigureAwait(true);
	}

	/// <summary>
	/// What the prompt said, as one of four outcomes.
	///
	/// <para><b>A finger that does not match is not an answer.</b> The
	/// prompt says so on the sheet itself and stays up for another try, so
	/// <c>OnAuthenticationFailed</c> is left alone; only the prompt giving
	/// up — after five, usually — arrives as an error, as a lockout.</para>
	/// </summary>
	private sealed class Answer : BiometricPrompt.AuthenticationCallback
	{
		private readonly TaskCompletionSource<FingerprintResult> _answered;

		public Answer(TaskCompletionSource<FingerprintResult> answered) => _answered = answered;

		public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) =>
			_answered.TrySetResult(new FingerprintResult(FingerprintOutcome.Confirmed));

		public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString)
		{
			var detail = errString?.ToString() ?? string.Empty;

			var outcome = errorCode switch
			{
				BiometricPrompt.ErrorNegativeButton
					or BiometricPrompt.ErrorUserCanceled
					or BiometricPrompt.ErrorCanceled
					or BiometricPrompt.ErrorTimeout => FingerprintOutcome.Cancelled,
				BiometricPrompt.ErrorNoBiometrics
					or BiometricPrompt.ErrorHwNotPresent
					or BiometricPrompt.ErrorHwUnavailable
					or BiometricPrompt.ErrorNoDeviceCredential => FingerprintOutcome.Unavailable,
				_ => FingerprintOutcome.Failed,
			};

			_answered.TrySetResult(new FingerprintResult(outcome, outcome == FingerprintOutcome.Cancelled ? string.Empty : detail));
		}
	}
}
