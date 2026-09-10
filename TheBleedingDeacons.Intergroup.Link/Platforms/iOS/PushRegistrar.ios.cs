namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half.
/// </summary>
/// <remarks>
/// <para>Both halves defer to <see cref="FirebasePush"/>, which owns the
/// awkward part: iOS issues an <i>APNs device token</i>, Fellowship sends
/// through FCM, and <c>message.token</c> needs an <i>FCM registration
/// token</i>. Firebase exchanges one for the other.</para>
///
/// <para><b>Reported from whether Firebase actually started, not from
/// whether this head was compiled with it.</b> A build without
/// <c>GoogleService-Info.plist</c>, or one whose plist names another
/// project, has no push and says so. That distinction is what lets the
/// settings indicator tell a member "this build has no push" rather than
/// "not registered yet", which would be an invitation to wait for
/// something that is never coming.</para>
///
/// <para>This file used to answer <c>false</c> and <c>null</c>
/// unconditionally. That was the honest answer while there was no Firebase
/// iOS SDK in the app; there is one now.</para>
/// </remarks>
public sealed partial class PushRegistrar
{
	// S3400: see the Android half. It cannot be a constant, because being
	// a method is what lets the two heads answer differently.
#pragma warning disable S3400
	private static partial bool PlatformSupported() => FirebasePush.Available;
#pragma warning restore S3400

	private partial Task<string?> PlatformTokenAsync() => FirebasePush.TokenAsync();
}
