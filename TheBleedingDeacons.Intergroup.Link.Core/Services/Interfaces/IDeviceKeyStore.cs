namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// This handset's keypair.
///
/// <para>Generated once, on first attach, and never sent anywhere except
/// its public half. Everything Fellowship sends this device is sealed to
/// it, and the server holds only the public key — so losing the private
/// half means losing every message already sent, permanently and for
/// everyone including the server. That is why
/// <see cref="RegenerateAsync"/> is a deliberate act rather than
/// something that happens on a whim.</para>
///
/// <para><b>Known limitation, stated rather than buried.</b> The Android
/// implementation generates the key in managed code and keeps the private
/// half in <c>SecureStorage</c>, which is encrypted at rest by a
/// hardware-backed Android Keystore key. That is meaningfully better than
/// a file in app storage and meaningfully worse than a non-exportable key
/// generated inside the TEE: here the private key exists as bytes in
/// process memory whenever a message is opened.</para>
///
/// <para>Moving generation into the Android Keystore proper is the next
/// step — <c>RSA/ECB/OAEPWithSHA-1AndMGF1Padding</c> is supported there,
/// which is exactly the padding Fellowship uses, so the wire format would
/// not change. This interface is the seam it goes behind and no caller
/// would need touching.</para>
/// </summary>
public interface IDeviceKeyStore
{
	/// <summary>Whether this handset already has a keypair.</summary>
	Task<bool> HasKeyAsync();

	/// <summary>
	/// Create a keypair, replacing any that exists, and return the public
	/// half as base64 SubjectPublicKeyInfo — the form Fellowship stores.
	/// </summary>
	Task<string> RegenerateAsync();

	/// <summary>The public half as base64 SPKI, or empty when there is none.</summary>
	Task<string> PublicKeyAsync();

	/// <summary>
	/// The private half as PKCS#8 PEM, or empty when there is none.
	///
	/// <para>Empty is the normal reading after a factory reset or a
	/// restored backup, not an exceptional one — which is why it answers
	/// empty rather than throwing. The caller turns that into a key-fault
	/// report.</para>
	/// </summary>
	Task<string> PrivateKeyAsync();

	/// <summary>Forget the keypair. Used on sign-out.</summary>
	Task ClearAsync();
}
