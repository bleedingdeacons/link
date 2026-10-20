using Android.Gms.Extensions;
using Android.Runtime;
using AndroidX.Core.Content;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using Firebase.Auth;
using Google.Android.Libraries.Identity.GoogleId;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Android half of <see cref="NativeGoogleSignIn"/>: Credential Manager's
/// "Sign in with Google" sheet, then Firebase Auth.
///
/// <para><b>Where the client id comes from.</b> Credential Manager asks for
/// an ID token minted for a <i>web</i> OAuth client, which Firebase creates
/// when Google sign-in is turned on for the project and writes into
/// <c>google-services.json</c>; the GoogleServicesJson build action turns
/// that into the <c>default_web_client_id</c> string resource. It is looked
/// up by name rather than through <c>Resource.String</c>, because a build
/// without the file — or with one from before Google sign-in was turned on —
/// has no such resource and must still compile. That build answers
/// unavailable, and Google goes through the browser as it always has.</para>
///
/// <para><b>Google also checks the signing certificate.</b> The SHA-1 of
/// whatever key signed this APK has to be registered on the Android app in
/// the Firebase console, or the sheet fails with a configuration error.
/// That surfaces here as unavailable rather than as an error, so a debug
/// build signed with an unregistered key still signs in, by the
/// browser.</para>
/// </summary>
public sealed partial class NativeGoogleSignIn
{
	private const string WebClientIdResource = "default_web_client_id";

	/// <summary>
	/// <c>GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL</c>, spelled
	/// out because the binding exposes Kotlin companion constants unevenly.
	/// </summary>
	private const string GoogleIdTokenType =
		"com.google.android.libraries.identity.googleid.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL";

	private static partial bool PlatformIsAvailable() => WebClientId().Length > 0;

	private static async partial Task<NativeGoogleSignInResult> PlatformSignInAsync(CancellationToken cancellationToken)
	{
		var activity = Platform.CurrentActivity;
		if (activity is null)
		{
			return NativeGoogleSignInResult.Unavailable;
		}

		string googleIdToken;
		try
		{
			googleIdToken = await PickAccountAsync(activity, WebClientId(), cancellationToken).ConfigureAwait(false);
		}
		catch (GetCredentialCancellationException)
		{
			return NativeGoogleSignInResult.Cancelled;
		}
		catch (OperationCanceledException)
		{
			return NativeGoogleSignInResult.Cancelled;
		}
#pragma warning disable CA1031 // Deliberately broad: every failure here has the same answer.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// No Google account and none added, Play Services missing or old,
			// an unregistered signing key. The browser can do all of these.
			Log.Warning(ex, "Native Google sign-in could not be completed; using the browser");
			return NativeGoogleSignInResult.Unavailable;
		}

		return await FirebaseIdTokenAsync(googleIdToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Raise Credential Manager's sheet and answer the Google ID token.
	/// </summary>
	private static async Task<string> PickAccountAsync(
		Android.App.Activity activity, string webClientId, CancellationToken cancellationToken)
	{
		// "Sign in with Google" rather than GetGoogleIdOption: the member
		// pressed a button to sign in, so they get the full account chooser
		// with "add another account", not a bottom sheet that only offers
		// accounts already used here and quietly fails when there are none.
		var option = new GetSignInWithGoogleOption.Builder(webClientId).Build();
		var request = new GetCredentialRequest([option]);

		var manager = CredentialManager.Create(activity);
		var signal = new Android.OS.CancellationSignal();
		await using var registration = cancellationToken.Register(signal.Cancel).ConfigureAwait(false);

		var completion = new TaskCompletionSource<GetCredentialResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		manager.GetCredentialAsync(
			activity,
			request,
			signal,
			ContextCompat.GetMainExecutor(activity) ?? throw new InvalidOperationException("No main executor."),
			new CredentialCallback(completion));

		var response = await completion.Task.ConfigureAwait(false);

		if (response.Credential is not CustomCredential custom || custom.Type != GoogleIdTokenType)
		{
			throw new InvalidOperationException($"Credential Manager answered a {response.Credential?.Type} credential.");
		}

		return GoogleIdTokenCredential.CreateFrom(custom.Data).IdToken;
	}

	/// <summary>
	/// Trade the Google ID token for a Firebase one, and sign straight back
	/// out of Firebase: Fellowship's device token is the credential this
	/// handset keeps, and a Firebase session left behind would only be a
	/// second thing to revoke.
	/// </summary>
	private static async Task<NativeGoogleSignInResult> FirebaseIdTokenAsync(string googleIdToken)
	{
		var auth = FirebaseAuth.Instance;

		try
		{
			var signedIn = await auth
				.SignInWithCredentialAsync(GoogleAuthProvider.GetCredential(googleIdToken, null))
				.ConfigureAwait(false);

			var user = signedIn?.User
				?? throw new InvalidOperationException("Firebase signed in with no user.");

			var token = await user.GetIdToken(true).AsAsync<GetTokenResult>().ConfigureAwait(false);

			return string.IsNullOrEmpty(token?.Token)
				? NativeGoogleSignInResult.Unavailable
				: NativeGoogleSignInResult.SignedIn(token.Token);
		}
#pragma warning disable CA1031 // Deliberately broad, as above.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// Google sign-in not enabled for the project, or the web client
			// id from another project. A configuration fault, not the
			// member's — the browser still works.
			Log.Error(ex, "Firebase refused the Google credential; using the browser");
			return NativeGoogleSignInResult.Unavailable;
		}
		finally
		{
			auth.SignOut();
		}
	}

	private static string WebClientId()
	{
		var context = Android.App.Application.Context;
		var id = context.Resources?.GetIdentifier(WebClientIdResource, "string", context.PackageName) ?? 0;

		return id == 0 ? string.Empty : context.GetString(id);
	}

	/// <summary>Credential Manager's callback, as a task.</summary>
	private sealed class CredentialCallback(TaskCompletionSource<GetCredentialResponse> completion)
		: Java.Lang.Object, ICredentialManagerCallback
	{
		public void OnResult(Java.Lang.Object? result)
		{
			var response = result?.JavaCast<GetCredentialResponse>();
			if (response is null)
			{
				completion.TrySetException(new InvalidOperationException("Credential Manager answered nothing."));
				return;
			}

			completion.TrySetResult(response);
		}

		public void OnError(Java.Lang.Object e)
		{
			// Matched on the Java class rather than the managed type: the
			// callback hands over a Java.Lang.Object, and whether it arrives
			// as the bound exception subtype depends on the marshaller. A
			// closed sheet must read as cancelled either way, or closing it
			// would open the browser.
			if (e.Class.Name.EndsWith(nameof(GetCredentialCancellationException), StringComparison.Ordinal))
			{
				completion.TrySetCanceled();
				return;
			}

			completion.TrySetException((object)e as Exception ?? new InvalidOperationException(e.ToString()));
		}
	}
}
