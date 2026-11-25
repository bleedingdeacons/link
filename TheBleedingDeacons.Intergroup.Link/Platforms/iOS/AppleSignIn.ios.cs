using AuthenticationServices;
using Foundation;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Support;
using UIKit;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half: Apple's own system sheet.
///
/// <para><b>Written against ASAuthorization directly, not MAUI's
/// AppleSignInAuthenticator, and the reason is the nonce.</b> The MAUI
/// helper's AuthenticateAsync takes no options of any kind — there is
/// nowhere to put one. Firebase compares the token's <c>nonce</c> claim
/// with the hash of the raw nonce it is given at <c>signInWithIdp</c>, so
/// a token minted without one is refused every single time.</para>
///
/// <para>The nonce is passed through <b>as given</b>. It arrives already
/// hashed (see <c>FirebaseFellowshipClient.StartSignInAsync</c>); Apple
/// copies it straight into the claim, and hashing it again here would
/// break the comparison.</para>
///
/// <para>Full name is not requested. Apple returns it once, on the very
/// first authorisation for an app, and never again — so anything relying
/// on it must persist it immediately or lose it. Link has no use for it:
/// the display name comes from Unity, which is the intergroup's record of
/// who this member is, and asking Apple for a second one would invite it
/// to disagree.</para>
/// </summary>
public sealed partial class AppleSignIn
{
	private const bool CompiledIn =
#if LINK_APPLE_SIGNIN
		true;
#else
		false;
#endif

	/// <summary>
	/// Whether this installation was signed with the entitlement.
	///
	/// <para>Read once, from the executable's own signature, because the
	/// same .ipa can go either way: CI builds it unsigned, and whoever
	/// signs it decides. Signed by the paid team, whose App ID has Sign in
	/// with Apple switched on, the signature carries
	/// <c>com.apple.developer.applesignin</c> and the button shows. Signed
	/// by a free personal team it cannot, and the button stays hidden
	/// rather than raising a sheet that always fails.</para>
	///
	/// <para>A build with no readable signature — the simulator, or a file
	/// this cannot parse — falls back to what it was compiled to assume:
	/// <c>-p:LinkPaidTeam=true</c> defines <c>LINK_APPLE_SIGNIN</c>.</para>
	/// </summary>
	private static readonly Lazy<bool> Entitled = new(ReadEntitlement);

	private static partial bool PlatformIsAvailable() =>
		OperatingSystem.IsIOSVersionAtLeast(13) && Entitled.Value;

	private static bool ReadEntitlement()
	{
		var keys = SignedEntitlementKeys();
		var entitled = keys?.Contains(CodeSignature.AppleSignIn) ?? CompiledIn;

		Log.Information(
			"Sign in with Apple {AppleSignInState} ({AppleSignInSource})",
			entitled ? "offered" : "not offered",
			keys is null ? "no readable signature; build default" : "from the signature");

		return entitled;
	}

	private static IReadOnlySet<string>? SignedEntitlementKeys()
	{
		var path = NSBundle.MainBundle.ExecutablePath;
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		try
		{
			using var executable = File.OpenRead(path);

			return CodeSignature.EntitlementKeys(executable);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Log.Warning(ex, "Could not read this app's own signature");
			return null;
		}
	}

	private partial Task<string?> PlatformGetIdTokenAsync(string nonce, CancellationToken cancellationToken)
	{
		var provider = new ASAuthorizationAppleIdProvider();
		var request = provider.CreateRequest();

		request.RequestedScopes = [ASAuthorizationScope.Email];
		request.Nonce = nonce;

		var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var handler = new AuthorizationHandler(completion);

		var controller = new ASAuthorizationController([request])
		{
			Delegate = handler,
			PresentationContextProvider = handler,
		};

		// Cancellation cannot reach into Apple's sheet — there is no API to
		// dismiss it — so this only stops us waiting. The sheet closing
		// afterwards resolves a completion nobody is listening to, which
		// TrySetResult tolerates.
		using var registration = cancellationToken.Register(() => completion.TrySetResult(null));

		controller.PerformRequests();

		return completion.Task;
	}

	/// <summary>
	/// Apple's callbacks, and the window to present over.
	///
	/// <para>One object for both roles because their lifetimes are the
	/// same, and because the controller holds only weak references to
	/// them: a delegate that is not rooted here is collected mid-sheet,
	/// and the sheet then closes into nothing. Held by the closure over
	/// this instance until the task completes.</para>
	/// </summary>
	private sealed class AuthorizationHandler(TaskCompletionSource<string?> completion)
		: NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
	{
		[Export("authorizationController:didCompleteWithAuthorization:")]
		public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
		{
			if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is not { } credential)
			{
				completion.TrySetResult(null);
				return;
			}

			// IdentityToken is the signed JWT, as raw UTF-8 bytes. It is
			// the only part of the credential this app sends anywhere:
			// the user identifier and email are in the token's own claims,
			// verified against Apple's JWKS by Fellowship, and taking them
			// from the credential instead would mean trusting the handset
			// for the thing the signature exists to establish.
			var token = credential.IdentityToken is { } data
				? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString()
				: null;

			completion.TrySetResult(string.IsNullOrEmpty(token) ? null : token);
		}

		[Export("authorizationController:didCompleteWithError:")]
		public void DidComplete(ASAuthorizationController controller, NSError error)
		{
			// Cancelling is by far the most common way here, and it is not
			// a failure — the shared half turns null into "the member
			// changed their mind" rather than an error on screen. A real
			// fault (no entitlement, no network) is indistinguishable from
			// here without parsing Apple's codes, and telling a member
			// which of those it was helps nobody.
			completion.TrySetResult(null);
		}

		public UIWindow GetPresentationAnchor(ASAuthorizationController controller) =>
			UIApplication.SharedApplication.ConnectedScenes
				.OfType<UIWindowScene>()
				.SelectMany(scene => scene.Windows)
				.FirstOrDefault(window => window.IsKeyWindow)
			?? UIApplication.SharedApplication.KeyWindow
			?? new UIWindow();
	}
}
