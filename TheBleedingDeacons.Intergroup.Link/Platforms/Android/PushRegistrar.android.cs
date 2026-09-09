using Android.Gms.Extensions;
using Firebase.Messaging;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Android half: Firebase Cloud Messaging.
/// </summary>
public sealed partial class PushRegistrar
{
	/// <summary>
	/// Firebase Cloud Messaging is compiled into this head, so the
	/// transport exists. Whether the build was given a
	/// <c>google-services.json</c> to talk to a project with is a separate
	/// question, and it shows up as an empty token rather than here — see
	/// the interface.
	/// </summary>
	// S3400 wants a constant instead of a method returning one. It cannot
	// be a constant: this is the implementing half of a partial method,
	// and being a method is what lets the iOS head answer differently.
	// Narrow suppression rather than a project-wide one.
#pragma warning disable S3400
	private static partial bool PlatformSupported() => true;
#pragma warning restore S3400

	private partial async Task<string?> PlatformTokenAsync()
	{
		// GetToken is marked obsolete by the binding because Google
		// deprecated it upstream, but it remains the only way to read the
		// current registration token and the documented Android guidance
		// still uses it. Narrow suppression rather than a project-wide
		// one, so a real replacement shows up as a warning here when the
		// binding gains it. Hand carries the same note for the same call.
#pragma warning disable CS0618
		var task = FirebaseMessaging.Instance.GetToken();
#pragma warning restore CS0618

		// AsAsync bridges the Google Play Services Task to a .NET one. A
		// null result is normal on a device with no Play Services and is
		// turned into "poll instead" by the shared half.
		var token = await task.AsAsync<Java.Lang.String>().ConfigureAwait(false);

		return token?.ToString();
	}
}
