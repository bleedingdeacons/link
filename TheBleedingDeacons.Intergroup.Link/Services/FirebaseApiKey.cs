using Serilog;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Firebase API key this build was given, read from the native configuration
/// (<c>google-services.json</c> or <c>GoogleService-Info.plist</c>) so there is one
/// place it comes from. Empty for a build without one, such as CI's.
/// </summary>
public static class FirebaseApiKey
{
	public static string Read()
	{
		try
		{
#if ANDROID
			return Firebase.FirebaseApp.Instance?.Options?.ApiKey ?? string.Empty;
#elif IOS
			return Firebase.Core.App.DefaultInstance?.Options?.ApiKey ?? string.Empty;
#else
			return string.Empty;
#endif
		}
#pragma warning disable CA1031 // Deliberately broad: an unconfigured Firebase throws its own platform's exception.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Information(ex, "This build has no Firebase configuration to read an API key from");

			return string.Empty;
		}
	}
}
