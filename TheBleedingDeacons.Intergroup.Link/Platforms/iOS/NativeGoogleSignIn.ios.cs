using Firebase.Auth;
using Foundation;
using Google.SignIn;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using UIKit;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// iOS half of <see cref="NativeGoogleSignIn"/>: Google's own sign-in
/// sheet, then Firebase Auth.
///
/// <para><b>Three things must be in the build, and each is checked rather
/// than assumed.</b> Firebase must have started (<see cref="FirebasePush"/>
/// owns that); <c>GoogleService-Info.plist</c> must carry a
/// <c>CLIENT_ID</c>, which Firebase adds only once Google sign-in is turned
/// on for the project; and Info.plist must register that client's
/// <c>REVERSED_CLIENT_ID</c> as a URL scheme, which CI writes in from the
/// same plist. GoogleSignIn raises an Objective-C exception — a launch-time
/// crash, not a refusal — when the scheme is missing, so this answers
/// unavailable instead of finding out. Unavailable means the browser, as
/// before.</para>
///
/// <para><b>No entitlement is needed.</b> Unlike Sign in with Apple this
/// works on a free personal team, so the sideloaded build gets it too.</para>
/// </summary>
public sealed partial class NativeGoogleSignIn
{
	private const string ConfigResource = "GoogleService-Info";

	/// <summary><c>kGIDSignInErrorCodeCanceled</c>.</summary>
	private const long CancelledCode = -5;

	private static partial bool PlatformIsAvailable()
	{
		if (!FirebasePush.Available)
		{
			return false;
		}

		var (clientId, reversed) = ClientIds();

		return clientId.Length > 0 && reversed.Length > 0 && RegistersUrlScheme(reversed);
	}

	private static async partial Task<NativeGoogleSignInResult> PlatformSignInAsync(CancellationToken cancellationToken)
	{
		SignInResult? picked;
		try
		{
			picked = await MainThread.InvokeOnMainThreadAsync(PickAccountAsync).ConfigureAwait(false);
		}
		catch (NSErrorException ex) when (ex.Code == CancelledCode)
		{
			return NativeGoogleSignInResult.Cancelled.Because(
				string.IsNullOrWhiteSpace(ex.Message) ? "Google's account picker was closed." : ex.Message);
		}
#pragma warning disable CA1031 // Deliberately broad: every failure here has the same answer.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Native Google sign-in could not be completed; using the browser");
			return NativeGoogleSignInResult.Unavailable;
		}

		var idToken = picked?.User?.IdToken?.TokenString;
		var accessToken = picked?.User?.AccessToken?.TokenString;
		if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(accessToken))
		{
			return NativeGoogleSignInResult.Unavailable;
		}

		return await FirebaseIdTokenAsync(idToken, accessToken).ConfigureAwait(false);
	}

	private static Task<SignInResult?> PickAccountAsync()
	{
		var presenter = Platform.GetCurrentUIViewController()
			?? throw new InvalidOperationException("No view controller to present the Google sheet from.");

		var signIn = SignIn.SharedInstance;
		signIn.Configuration = new Configuration(ClientIds().ClientId);

		var completion = new TaskCompletionSource<SignInResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
		signIn.SignInWithPresentingViewController(presenter, (result, error) =>
		{
			if (error is not null)
			{
				completion.TrySetException(new NSErrorException(error));
				return;
			}

			completion.TrySetResult(result);
		});

		return completion.Task;
	}

	/// <summary>
	/// Trade the Google tokens for a Firebase ID token, and sign straight
	/// back out of both: Fellowship's device token is what this handset keeps,
	/// and the next sign-in on a shared phone should ask whose it is rather
	/// than reuse whoever was last.
	/// </summary>
	private static async Task<NativeGoogleSignInResult> FirebaseIdTokenAsync(string idToken, string accessToken)
	{
		var auth = Auth.DefaultInstance;

		try
		{
			var signedIn = await auth
				.SignInWithCredentialAsync(GoogleAuthProvider.GetCredential(idToken, accessToken))
				.ConfigureAwait(false);

			var token = await signedIn.User.GetIdTokenAsync(true).ConfigureAwait(false);

			return string.IsNullOrEmpty(token)
				? NativeGoogleSignInResult.Unavailable
				: NativeGoogleSignInResult.SignedIn(token);
		}
#pragma warning disable CA1031 // Deliberately broad, as above.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// Google sign-in not enabled for the project, or a client id from
			// another one. A configuration fault, not the member's.
			Log.Error(ex, "Firebase refused the Google credential; using the browser");
			return NativeGoogleSignInResult.Unavailable;
		}
		finally
		{
			auth.SignOut(out _);
			SignIn.SharedInstance.SignOutUser();
		}
	}

	/// <summary>
	/// The OAuth client Firebase created for this app, read from the plist
	/// itself so its absence is a value rather than an exception.
	/// </summary>
	private static (string ClientId, string Reversed) ClientIds()
	{
		var path = NSBundle.MainBundle.PathForResource(ConfigResource, "plist");
		if (path is null || NSDictionary.FromFile(path) is not { } plist)
		{
			return (string.Empty, string.Empty);
		}

		return (plist["CLIENT_ID"]?.ToString() ?? string.Empty, plist["REVERSED_CLIENT_ID"]?.ToString() ?? string.Empty);
	}

	private static bool RegistersUrlScheme(string scheme)
	{
		if (NSBundle.MainBundle.InfoDictionary["CFBundleURLTypes"] is not NSArray types)
		{
			return false;
		}

		for (nuint i = 0; i < types.Count; i++)
		{
			if (types.GetItem<NSDictionary>(i)?["CFBundleURLSchemes"] is not NSArray schemes)
			{
				continue;
			}

			for (nuint j = 0; j < schemes.Count; j++)
			{
				if (string.Equals(schemes.GetItem<NSString>(j)?.ToString(), scheme, StringComparison.Ordinal))
				{
					return true;
				}
			}
		}

		return false;
	}
}
