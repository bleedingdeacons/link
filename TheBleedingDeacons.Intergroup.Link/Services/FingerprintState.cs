using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Whether fingerprint sign-in is set up, and whether the member has
/// signed out behind it. See <see cref="FingerprintSignIn"/>.
///
/// <para>In <see cref="Preferences"/>, as the screen preferences are,
/// rather than in <c>SecureStorage</c> beside the session. These are not
/// secrets: knowing that a phone is locked gets nobody anything, and the
/// lock guards this app's screens rather than the token, which stays in
/// SecureStorage either way. They are read on every launch, and Android's
/// SecureStorage is the keystore — a slow place to keep two bools.</para>
/// </summary>
public sealed class FingerprintState : IFingerprintState
{
	private const string EnabledKey = "fingerprint_sign_in";
	private const string LockedKey = "fingerprint_locked";

	public bool Enabled
	{
		get => Preferences.Default.Get(EnabledKey, false);
		set => Preferences.Default.Set(EnabledKey, value);
	}

	public bool Locked
	{
		get => Preferences.Default.Get(LockedKey, false);
		set => Preferences.Default.Set(LockedKey, value);
	}
}
