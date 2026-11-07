using System.Security.Cryptography;
using Serilog;
#if ANDROID
using Android.Content.PM;
#endif

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The headers that tell Google which app is using the Firebase API key.
///
/// <para><b>Why they're needed.</b> The key in <c>google-services.json</c> only accepts
/// calls from this Android app, signed with a certificate the key lists. Firebase's own
/// SDK says which app is calling on every request. Link's own calls to sign in to
/// Firebase and refresh its token don't go through the SDK, so without these headers
/// Google answers 403 "Requests from this Android client application &lt;empty&gt; are
/// blocked", and nobody can sign in.</para>
///
/// <para><b>The certificate is read from the phone</b>, from the app as installed, so a
/// Play build sends Google's app signing certificate and a local build sends the debug
/// one. Each has to be listed on the key. Nothing here is secret: the package and the
/// fingerprint are both printed on the Play listing's own pages.</para>
/// </summary>
public static class FirebaseClientHeaders
{
	public static IReadOnlyDictionary<string, string> Read()
	{
		try
		{
#if ANDROID
			return Android();
#elif IOS
			var bundle = Foundation.NSBundle.MainBundle.BundleIdentifier ?? string.Empty;

			return bundle.Length == 0
				? new Dictionary<string, string>(StringComparer.Ordinal)
				: new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Ios-Bundle-Identifier"] = bundle };
#else
			return new Dictionary<string, string>(StringComparer.Ordinal);
#endif
		}
#pragma warning disable CA1031 // Deliberately broad: without the headers the key is refused, which is said plainly elsewhere.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Could not say which app is calling Google; signing in to Firebase will be refused");

			return new Dictionary<string, string>(StringComparer.Ordinal);
		}
	}

#if ANDROID
	private static Dictionary<string, string> Android()
	{
		var context = global::Android.App.Application.Context;
		var package = context.PackageName ?? string.Empty;
		var certificate = SigningCertificate(context.PackageManager!, package);

		if (package.Length == 0 || certificate is null)
		{
			Log.Warning("This app's package or signing certificate could not be read; signing in to Firebase will be refused");

			return new Dictionary<string, string>(StringComparer.Ordinal);
		}

		var fingerprint = Convert.ToHexString(SHA1.HashData(certificate));

		// Not secret, and the first thing to compare with the key's list when Google
		// still refuses: a build signed with a certificate the key doesn't list.
		Log.Information("Calls to Google name this app as {Package}, signed with {Fingerprint}", package, fingerprint);

		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["X-Android-Package"] = package,
			["X-Android-Cert"] = fingerprint,
		};
	}

	/// <summary>The certificate the installed app is signed with now.</summary>
	private static byte[]? SigningCertificate(PackageManager packages, string package)
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(28))
		{
			var info = OperatingSystem.IsAndroidVersionAtLeast(33)
				? packages.GetPackageInfo(package, PackageManager.PackageInfoFlags.Of((long)PackageInfoFlags.SigningCertificates))
				: GetPackageInfoBefore33(packages, package, PackageInfoFlags.SigningCertificates);

			return info?.SigningInfo?.GetApkContentsSigners()?.FirstOrDefault()?.ToByteArray();
		}

		// Before Android 9 the only way to ask. Deprecated since, and still answered.
#pragma warning disable CA1422, CS0618
		return GetPackageInfoBefore33(packages, package, PackageInfoFlags.Signatures)?.Signatures?.FirstOrDefault()?.ToByteArray();
#pragma warning restore CA1422, CS0618
	}

#pragma warning disable CA1422 // Only called where Android 13's replacement doesn't exist.
	private static PackageInfo? GetPackageInfoBefore33(PackageManager packages, string package, PackageInfoFlags flags) =>
		packages.GetPackageInfo(package, flags);
#pragma warning restore CA1422
#endif
}
