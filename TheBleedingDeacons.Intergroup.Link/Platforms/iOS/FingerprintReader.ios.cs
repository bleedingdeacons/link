using Foundation;
using LocalAuthentication;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half: <c>LAContext</c>, which is Touch ID on a phone with a
/// home button and Face ID on one without.
///
/// <para>Set up only on a phone with Touch ID or Face ID enrolled, and
/// confirmed with either, falling back to the phone's passcode as the
/// Android half falls back to its PIN. Face ID needs
/// <c>NSFaceIDUsageDescription</c> in Info.plist, without which iOS ends
/// the app the moment it is asked.</para>
/// </summary>
public sealed partial class FingerprintReader
{
	private const LAPolicy Biometrics = LAPolicy.DeviceOwnerAuthenticationWithBiometrics;

	/// <summary>Biometrics, then the passcode when they are not taken.</summary>
	private const LAPolicy BiometricsOrPasscode = LAPolicy.DeviceOwnerAuthentication;

	private static partial bool PlatformIsAvailable()
	{
		using var context = new LAContext();

		return context.CanEvaluatePolicy(Biometrics, out _);
	}

	private static partial bool PlatformCanConfirm()
	{
		using var context = new LAContext();

		return context.CanEvaluatePolicy(BiometricsOrPasscode, out _);
	}

	private static partial async Task<FingerprintResult> PlatformConfirmAsync(
		string title,
		string subtitle,
		CancellationToken cancellationToken)
	{
		using var context = new LAContext
		{
			// Left at iOS's own "Enter Passcode" fallback; see the class remarks.
			LocalizedCancelTitle = "Cancel",
		};

		using var cancelled = cancellationToken.Register(context.Invalidate);

		// iOS draws its own heading, so the title has nowhere to go; the
		// subtitle is the reason line under it.
		var (confirmed, error) = await context.EvaluatePolicyAsync(BiometricsOrPasscode, subtitle).ConfigureAwait(true);
		if (confirmed)
		{
			return new FingerprintResult(FingerprintOutcome.Confirmed);
		}

		return Outcome(error);
	}

	private static FingerprintResult Outcome(NSError? error)
	{
		var status = error is null ? LAStatus.AuthenticationFailed : (LAStatus)(long)error.Code;

		return status switch
		{
			LAStatus.UserCancel or LAStatus.SystemCancel or LAStatus.AppCancel or LAStatus.UserFallback =>
				new FingerprintResult(FingerprintOutcome.Cancelled),
			LAStatus.BiometryNotAvailable or LAStatus.BiometryNotEnrolled or LAStatus.PasscodeNotSet =>
				FingerprintResult.Unavailable(error?.LocalizedDescription ?? string.Empty),
			_ => new FingerprintResult(FingerprintOutcome.Failed, error?.LocalizedDescription ?? string.Empty),
		};
	}
}
