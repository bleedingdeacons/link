namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The iOS half, which does not exist yet.
///
/// <para>Answering empty is not a stub that will crash later — it is the
/// documented "this handset collects its own messages" state, and the
/// whole app works in it. What an iOS build is missing is the *speed* of
/// push, not the messages.</para>
///
/// <para>Finishing it needs the Firebase iOS SDK, an APNs key on the
/// Firebase project, and the background-fetch entitlement. See
/// README.md, "What is not done".</para>
/// </summary>
public sealed partial class PushRegistrar
{
	/// <summary>
	/// No transport on this head, and saying so is the point: it is what
	/// lets the settings indicator tell an iOS member "this build has no
	/// push" instead of "not registered yet", which would be an invitation
	/// to wait for something that is never coming.
	/// </summary>
	// S3400: see the Android half. It cannot be a constant, because being
	// a method is what lets the two heads answer differently.
#pragma warning disable S3400
	private static partial bool PlatformSupported() => false;
#pragma warning restore S3400

	private partial Task<string?> PlatformTokenAsync() => Task.FromResult<string?>(null);
}
