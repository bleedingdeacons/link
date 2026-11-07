using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// The backup file: gzipped JSON, sealed with AES-256-GCM under a key from
/// the member's passphrase.
///
/// <para><b>The layout</b>, all numbers big-endian. Everything up to the tag
/// is the header, and the header is the additional authenticated data, so
/// changing any byte of it stops the file opening:</para>
///
/// <code>
/// offset  size  what
///      0     4  "LKBK"
///      4     2  format version
///      6     4  PBKDF2-SHA256 iterations
///     10    16  salt
///     26    12  nonce
///     38    16  GCM tag
///     54     n  ciphertext
/// </code>
///
/// <para><b>Versioned from the first file.</b> A later version adds to the
/// JSON, and whatever reads it has to go on reading this one.
/// <c>Fixtures/backup-v1.linkbackup</c> in Link.Tests holds that to account,
/// so do not change the layout of version 1: raise the version. Version 2 has
/// the same layout and adds the device to the JSON.</para>
///
/// <para><b>Every failure is the same null.</b> A wrong passphrase and a
/// damaged file cannot be told apart, because GCM only says that the tag did
/// not match. So <see cref="Open"/> does not pretend it knows which it was,
/// and the member gets one message.</para>
///
/// <para>Takes bytes and a passphrase and touches nothing else, so it is
/// tested here. Where the bytes go is the app's: <c>IBackupFiles</c>.</para>
/// </summary>
public static class BackupFile
{
	/// <summary>The version this build writes.</summary>
	public const int FormatVersion = 2;

	/// <summary>The oldest version this build still reads.</summary>
	public const int OldestReadableVersion = 1;

	/// <summary>What the file's name ends in, dot included.</summary>
	public const string Extension = ".linkbackup";

	public const int MinimumPassphraseLength = 12;

	/// <summary>
	/// The largest file this will try to open. A backup holds a few hundred
	/// short messages, so this is far beyond any real one.
	/// </summary>
	public const int MaximumFileBytes = 64 * 1024 * 1024;

	/// <summary>
	/// How hard a guess has to work, and why sealing and opening each take
	/// most of a second on a phone. The file leaves Link, so somebody holding
	/// it can guess at the passphrase for as long as they like.
	/// </summary>
	private const int Iterations = 600_000;

	/// <summary>
	/// The most a file may ask for. The iteration count is read before
	/// anything is authenticated, so without a ceiling a damaged byte could
	/// hold the phone in key derivation for hours.
	/// </summary>
	private const int MaximumIterations = 10_000_000;

	private const int MaximumInflatedBytes = 256 * 1024 * 1024;

	private const int KeyBytes = 32;
	private const int SaltBytes = 16;
	private const int NonceBytes = 12;
	private const int TagBytes = 16;

	private const int VersionOffset = 4;
	private const int IterationsOffset = 6;
	private const int SaltOffset = 10;
	private const int NonceOffset = 26;
	private const int HeaderBytes = 38;
	private const int CiphertextOffset = HeaderBytes + TagBytes;

	private static readonly byte[] Magic = "LKBK"u8.ToArray();

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	/// <summary>The name a backup made at this moment goes by, on this phone's calendar.</summary>
	public static string FileNameFor(DateTimeOffset at) =>
		"link-backup-" + at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension;

	/// <summary>
	/// What is wrong with this passphrase, in the member's words, or null
	/// when it will do.
	/// </summary>
	/// <param name="passphrase">What was typed first.</param>
	/// <param name="again">What was typed the second time.</param>
	public static string? PassphraseProblem(string? passphrase, string? again)
	{
		if (!IsLongEnough(passphrase))
		{
			return string.Create(
				CultureInfo.InvariantCulture,
				$"Use a passphrase of at least {MinimumPassphraseLength} characters.");
		}

		return string.Equals(Normalise(passphrase!), Normalise(again ?? string.Empty), StringComparison.Ordinal)
			? null
			: "The two passphrases do not match.";
	}

	/// <summary>
	/// Seal these contents under this passphrase.
	///
	/// <para>Slow on purpose, and not something to run on the main thread:
	/// see <see cref="Iterations"/>.</para>
	/// </summary>
	/// <exception cref="ArgumentException">The passphrase is too short. The
	/// screen checks first, so this is a wiring mistake rather than something
	/// a member can reach.</exception>
	public static byte[] Seal(BackupContents contents, string passphrase)
	{
		ArgumentNullException.ThrowIfNull(contents);

		if (!IsLongEnough(passphrase))
		{
			throw new ArgumentException("The passphrase is too short.", nameof(passphrase));
		}

		var plaintext = Compress(JsonSerializer.SerializeToUtf8Bytes(contents with { FormatVersion = FormatVersion }, JsonOptions));

		var file = new byte[CiphertextOffset + plaintext.Length];

		Magic.CopyTo(file, 0);
		BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(VersionOffset), FormatVersion);
		BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(IterationsOffset), Iterations);
		RandomNumberGenerator.Fill(file.AsSpan(SaltOffset, SaltBytes));
		RandomNumberGenerator.Fill(file.AsSpan(NonceOffset, NonceBytes));

		var key = DeriveKey(passphrase, file.AsSpan(SaltOffset, SaltBytes), Iterations);

		try
		{
			using var gcm = new AesGcm(key, TagBytes);
			gcm.Encrypt(
				file.AsSpan(NonceOffset, NonceBytes),
				plaintext,
				file.AsSpan(CiphertextOffset),
				file.AsSpan(HeaderBytes, TagBytes),
				file.AsSpan(0, HeaderBytes));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}

		return file;
	}

	/// <summary>
	/// Open a backup, or answer null for a wrong passphrase, a damaged file,
	/// something that is not a backup at all, or a version this build does
	/// not read. Never throws for anything a file can contain.
	/// </summary>
	public static BackupContents? Open(byte[] file, string passphrase)
	{
		ArgumentNullException.ThrowIfNull(file);

		if (!IsReadable(file, out var version, out var iterations) || string.IsNullOrEmpty(passphrase))
		{
			return null;
		}

		var plaintext = new byte[file.Length - CiphertextOffset];
		var key = DeriveKey(passphrase, file.AsSpan(SaltOffset, SaltBytes), iterations);

		try
		{
			using var gcm = new AesGcm(key, TagBytes);
			gcm.Decrypt(
				file.AsSpan(NonceOffset, NonceBytes),
				file.AsSpan(CiphertextOffset),
				file.AsSpan(HeaderBytes, TagBytes),
				plaintext,
				file.AsSpan(0, HeaderBytes));

			return Parse(Decompress(plaintext), version);
		}
		catch (Exception e) when (e is CryptographicException or InvalidDataException or JsonException)
		{
			return null;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	/// <summary>
	/// Whether the header is one this build could try, before spending most
	/// of a second deriving a key from it.
	/// </summary>
	/// <remarks>
	/// The floor is not a defence of the key, since a lower count only derives
	/// a different one and the tag fails. It is here so that this build never
	/// accepts a file weaker than the ones it writes.
	/// </remarks>
	private static bool IsReadable(byte[] file, out int version, out int iterations)
	{
		version = 0;
		iterations = 0;

		if (file.Length < CiphertextOffset
			|| file.Length > MaximumFileBytes
			|| !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
		{
			return false;
		}

		version = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(VersionOffset));

		if (version is < OldestReadableVersion or > FormatVersion)
		{
			return false;
		}

		var asked = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(IterationsOffset));

		if (asked is < Iterations or > MaximumIterations)
		{
			return false;
		}

		iterations = (int)asked;

		return true;
	}

	/// <summary>
	/// Read the JSON, and refuse what could not have come from
	/// <see cref="Seal"/>: a version that disagrees with the header, no
	/// member, no history.
	///
	/// <para>A device that isn't whole counts as no device, which restores the
	/// messages and enrols as new. So does one in a version 1 file, since no
	/// version 1 file was written with one.</para>
	/// </summary>
	private static BackupContents? Parse(byte[]? json, int version)
	{
		if (json is null)
		{
			return null;
		}

		var contents = JsonSerializer.Deserialize<BackupContents>(json, JsonOptions);

		if (contents is not { MemberId: > 0, History: { } history }
			|| contents.FormatVersion != version
			|| history.Messages is null
			|| history.Sent is null
			|| history.Deleted is null)
		{
			return null;
		}

		return version >= 2 && contents.Device is { Id.Length: > 0, PrivateKey.Length: > 0 }
			? contents
			: contents with { Device = null };
	}

	private static bool IsLongEnough(string? passphrase) =>
		passphrase is not null && Normalise(passphrase).Length >= MinimumPassphraseLength;

	/// <summary>
	/// The same passphrase has to give the same key however it was typed, on
	/// whichever phone.
	///
	/// <para><b>Trimmed</b>, because a phone keyboard adds a space after a
	/// suggested word, and a space nobody knows they typed is a backup nobody
	/// can open. <b>Composed</b>, because iOS and Android keyboards can spell
	/// the same accented letter as different bytes. Text that cannot be
	/// composed, which is half a surrogate pair, stays as it was typed.</para>
	/// </summary>
	private static string Normalise(string passphrase)
	{
		var trimmed = passphrase.Trim();

		try
		{
			return trimmed.Normalize(NormalizationForm.FormC);
		}
		catch (ArgumentException)
		{
			return trimmed;
		}
	}

	private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations)
	{
		var secret = Encoding.UTF8.GetBytes(Normalise(passphrase));

		try
		{
			return Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(secret);
		}
	}

	private static byte[] Compress(byte[] json)
	{
		using var output = new MemoryStream();

		using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
		{
			gzip.Write(json);
		}

		return output.ToArray();
	}

	/// <summary>Inflate, or answer null if it comes to more than any backup could.</summary>
	private static byte[]? Decompress(byte[] compressed)
	{
		using var source = new MemoryStream(compressed);
		using var gzip = new GZipStream(source, CompressionMode.Decompress);
		using var output = new MemoryStream();

		var buffer = new byte[81920];
		int read;

		while ((read = gzip.Read(buffer)) > 0)
		{
			if (output.Length + read > MaximumInflatedBytes)
			{
				return null;
			}

			output.Write(buffer, 0, read);
		}

		return output.ToArray();
	}
}
