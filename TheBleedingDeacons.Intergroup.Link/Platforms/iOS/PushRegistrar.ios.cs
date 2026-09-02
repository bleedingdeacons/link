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
	private partial Task<string?> PlatformTokenAsync() => Task.FromResult<string?>(null);
}
