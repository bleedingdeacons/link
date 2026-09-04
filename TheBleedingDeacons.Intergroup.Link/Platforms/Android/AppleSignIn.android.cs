namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The Android half: there isn't one.
///
/// <para>Apple's sheet is a system service on Apple's own platforms.
/// Android could reach Sign in with Apple through its <i>web</i> flow,
/// which is a browser leg and a client secret — the very thing the
/// client-side path exists to avoid — so it is not offered rather than
/// offered badly. An Android member signs in with Google.</para>
/// </summary>
public sealed partial class AppleSignIn
{
	// S3400 wants a constant instead of a method returning one. It cannot
	// be a constant: this is one half of a partial whose other half, on
	// iOS, does real work. The rule is right in general and wrong about
	// the one shape that makes a cross-platform seam.
#pragma warning disable S3400
	private static partial bool PlatformIsAvailable() => false;
#pragma warning restore S3400

	private partial Task<string?> PlatformGetIdTokenAsync(string nonce, CancellationToken cancellationToken) =>
		Task.FromResult<string?>(null);
}
