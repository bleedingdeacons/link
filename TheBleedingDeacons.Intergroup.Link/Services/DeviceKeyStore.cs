using System.Security.Cryptography;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// This handset's keypair, kept in <see cref="SecureStorage"/>.
///
/// <para>Generated once, on first attach. The public half goes to
/// Fellowship at enrolment and everything the server sends this device is
/// sealed to it; the private half is what opens those messages and never
/// leaves the handset.</para>
///
/// <para>See <see cref="IDeviceKeyStore"/> for the storage limitation
/// this carries and the hardware-backed step that would remove it.</para>
/// </summary>
public sealed class DeviceKeyStore : IDeviceKeyStore
{
	private const string PrivateKeyName = "link_device_private_key";
	private const string PublicKeyName = "link_device_public_key";

	private const int KeyBits = 2048;

	public async Task<bool> HasKeyAsync() => !string.IsNullOrEmpty(await PrivateKeyAsync().ConfigureAwait(false));

	/// <summary>
	/// Create a keypair, replacing any that exists, and return the public
	/// half as base64 SubjectPublicKeyInfo.
	///
	/// <para>Replacing is destructive in a way worth being clear about:
	/// every message already sealed to the old key becomes unreadable, and
	/// nobody can undo that — Fellowship never held the private half
	/// either, so it cannot re-seal them. That is why this is called at
	/// enrolment and from an explicit "my messages will not open"
	/// recovery, and never automatically.</para>
	/// </summary>
	public async Task<string> RegenerateAsync()
	{
		using var rsa = RSA.Create(KeyBits);

		var privatePem = rsa.ExportPkcs8PrivateKeyPem();
		var publicSpki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

		await SecureStorage.SetAsync(PrivateKeyName, privatePem).ConfigureAwait(false);
		await SecureStorage.SetAsync(PublicKeyName, publicSpki).ConfigureAwait(false);

		return publicSpki;
	}

	public Task<string> PublicKeyAsync() => ReadAsync(PublicKeyName);

	public Task<string> PrivateKeyAsync() => ReadAsync(PrivateKeyName);

	public Task ClearAsync()
	{
		SecureStorage.Remove(PrivateKeyName);
		SecureStorage.Remove(PublicKeyName);

		return Task.CompletedTask;
	}

	/// <summary>
	/// Read one value, answering empty for anything that is not there.
	///
	/// <para>An empty read is the normal state after a factory reset, a
	/// restored backup, or a keystore invalidated because the screen lock
	/// changed — not an exceptional one. So it answers empty rather than
	/// throwing, and the caller turns that into a key-fault report. On
	/// some Android builds <c>SecureStorage</c> throws rather than
	/// returning null when its own key has been invalidated, which is the
	/// same situation wearing a different coat.</para>
	/// </summary>
	private static async Task<string> ReadAsync(string name)
	{
		try
		{
			return await SecureStorage.GetAsync(name).ConfigureAwait(false) ?? string.Empty;
		}
		catch (Exception e) when (e is System.Security.SecurityException
			or CryptographicException
			or InvalidOperationException)
		{
			return string.Empty;
		}
	}
}
