using System.Security.Cryptography;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What is inside a backup, once it has been opened.
///
/// <para><b>Nothing here signs in, and nothing here may ever.</b> No device
/// token and no refresh token. A copied phone must never hold a working
/// credential, which is the reason <c>android:allowBackup</c> is false in the
/// manifest, and restoring always needs a sign-in as the same member. A test
/// holds the property names of this file to a fixed list, so adding one means
/// changing that list on purpose.</para>
///
/// <para><b>It does hold this phone's private key</b>, since version 2. That is
/// what lets a new phone take the old one's place and read what was sealed to
/// it, and it is why the passphrase matters more than it did. The key opens
/// messages. It proves nothing to anybody. See <see cref="BackupDevice"/>.</para>
/// </summary>
public sealed record BackupContents
{
	public int FormatVersion { get; init; } = BackupFile.FormatVersion;

	/// <summary>The member whose messages these are.</summary>
	public long MemberId { get; init; }

	/// <summary>When the backup was made, in Unix seconds.</summary>
	public long CreatedAt { get; init; }

	public HistorySnapshot? History { get; init; }

	public BackupSettings? Settings { get; init; }

	/// <summary>
	/// Which device this phone was, for taking its place. Absent from every
	/// version 1 file, and from a backup made on a phone with no key.
	/// </summary>
	public BackupDevice? Device { get; init; }
}

/// <summary>
/// The device a backup came from: its id and its private key.
///
/// <para><b>Neither of these signs in.</b> Taking a device over needs a fresh
/// sign-in as the member it belongs to, and the key has to be the one the device
/// holds now, so a backup from before a key replacement can't do it. An admin's
/// revoke stays final too. See <c>takeOverDevice</c> in Fellowship's Functions.
/// </para>
/// </summary>
public sealed record BackupDevice
{
	/// <summary>The Firebase device id, <c>d_</c> and hex.</summary>
	public string Id { get; init; } = string.Empty;

	/// <summary>The private half, as PKCS#8 PEM.</summary>
	public string PrivateKey { get; init; } = string.Empty;

	/// <summary>
	/// The public half as base64 SubjectPublicKeyInfo, which is the form the device
	/// holds it in, or empty when the key can't be read.
	/// </summary>
	public string PublicKey()
	{
		try
		{
			using var rsa = RSA.Create();
			rsa.ImportFromPem(PrivateKey);

			return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
		}
		catch (Exception e) when (e is CryptographicException or ArgumentException)
		{
			return string.Empty;
		}
	}

	/// <summary>A record prints its members by default, and this one must never be printed.</summary>
	public override string ToString() => $"BackupDevice {{ Id = {Id} }}";
}

/// <summary>
/// The choices about the screen that travel with a backup.
///
/// <para><b>Fingerprint sign-in does not.</b> It is a sign-in, and only a
/// finger this phone has just matched turns it on. Carrying it would switch a
/// lock on for a phone that has never read the member's finger.</para>
///
/// <para>Each is nullable so that a value this build does not know, or one a
/// later version stops writing, leaves the member's current choice alone.</para>
/// </summary>
public sealed record BackupSettings
{
	/// <summary>
	/// The <see cref="LinkStyle"/>, as the number <c>ThemeChoice</c> keeps it
	/// as, so that the JSON needs no converter built at runtime for the enum.
	/// That is the one thing here an iOS ahead-of-time build could trip over.
	/// </summary>
	public int? Style { get; init; }

	public bool? Sound { get; init; }

	public bool? OnlyRegistered { get; init; }

	public bool? PullToRefresh { get; init; }
}
