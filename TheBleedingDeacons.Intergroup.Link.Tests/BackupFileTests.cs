using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The file itself: what it opens to, what stops it opening, and what its
/// bytes actually are.
///
/// <para><see cref="BackupBytes"/> reads the layout the way the README writes
/// it, without calling <see cref="BackupFile"/>. A change to the layout turns
/// this red, not just the shipping code agreeing with itself.</para>
/// </summary>
public class BackupFileTests
{
	private const string Passphrase = "a long enough passphrase";

	[Fact]
	public void What_is_sealed_opens_to_the_same_thing()
	{
		var opened = BackupFile.Open(BackupFile.Seal(Contents(), Passphrase), Passphrase);

		Assert.NotNull(opened);
		Assert.Equal(BackupFile.FormatVersion, opened.FormatVersion);
		Assert.Equal(7, opened.MemberId);
		Assert.Equal(1_790_000_000, opened.CreatedAt);

		var history = opened.History!;

		Assert.Equal("Train times", Assert.Single(history.Messages).Subject);
		Assert.Equal("Jo B", Assert.Single(history.Sent).To);
		Assert.Equal(new DeletedMessage(11, 10), Assert.Single(history.Deleted));
		Assert.Equal(9, history.ClearedUpTo);
		Assert.Equal(8, history.SentClearedUpTo);
		Assert.Equal(13, history.PolledUpTo);

		Assert.Equal((int)LinkStyle.SiloTerminal, opened.Settings!.Style);
		Assert.False(opened.Settings.Sound);
		Assert.True(opened.Settings.PullToRefresh);
	}

	[Fact]
	public void Two_files_from_the_same_contents_share_no_salt_or_nonce()
	{
		// A repeated key and nonce is how GCM fails, so neither may repeat.
		var first = BackupBytes.Read(BackupFile.Seal(Contents(), Passphrase));
		var second = BackupBytes.Read(BackupFile.Seal(Contents(), Passphrase));

		Assert.NotEqual(first.Salt, second.Salt);
		Assert.NotEqual(first.Nonce, second.Nonce);
	}

	[Fact]
	public void The_layout_is_the_one_written_down()
	{
		var file = BackupFile.Seal(Contents(), Passphrase);
		var bytes = BackupBytes.Read(file);

		Assert.Equal("LKBK", Encoding.ASCII.GetString(file, 0, 4));
		Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(4)));
		Assert.Equal(600_000, bytes.Iterations);

		using var document = JsonDocument.Parse(bytes.Plaintext(Passphrase));

		Assert.Equal(2, document.RootElement.GetProperty("formatVersion").GetInt32());
		Assert.Equal(7, document.RootElement.GetProperty("memberId").GetInt64());
	}

	[Fact]
	public void The_message_text_is_not_readable_in_the_file()
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		Assert.DoesNotContain("Train times", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
	}

	[Fact]
	public void A_wrong_passphrase_opens_nothing()
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		Assert.Null(BackupFile.Open(file, "a different passphrase"));
		Assert.Null(BackupFile.Open(file, string.Empty));
	}

	[Theory]
	[InlineData(0)] // Magic
	[InlineData(3)]
	[InlineData(4)] // Version
	[InlineData(5)]
	[InlineData(6)] // Iterations
	[InlineData(7)]
	[InlineData(8)]
	[InlineData(9)]
	[InlineData(10)] // Salt
	[InlineData(25)]
	[InlineData(26)] // Nonce
	[InlineData(37)]
	[InlineData(38)] // Tag
	[InlineData(53)]
	[InlineData(54)] // Ciphertext
	public void Changing_one_byte_anywhere_stops_it_opening(int offset)
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		file[offset] ^= 0x01;

		Assert.Null(BackupFile.Open(file, Passphrase));
	}

	[Fact]
	public void The_tag_covers_the_header()
	{
		// The theory above cannot show this on its own. A changed salt or
		// iteration count derives a different key, a changed nonce fails by
		// itself, and a changed magic or version is refused by looking at it,
		// so all of them would pass with no additional data at all. A tag
		// that will not verify without the header is what shows it is there.
		var bytes = BackupBytes.Read(BackupFile.Seal(Contents(), Passphrase));

		Assert.NotNull(bytes.Plaintext(Passphrase));
		Assert.Throws<AuthenticationTagMismatchException>(() => bytes.PlaintextWithoutHeader(Passphrase));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(10)]
	[InlineData(53)]
	public void A_cut_off_file_does_not_open(int length)
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		Assert.Null(BackupFile.Open(file[..length], Passphrase));
	}

	[Fact]
	public void A_file_cut_short_in_the_ciphertext_does_not_open()
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		Assert.Null(BackupFile.Open(file[..^1], Passphrase));
	}

	[Fact]
	public void Something_that_is_not_a_backup_does_not_open()
	{
		Assert.Null(BackupFile.Open(RandomNumberGenerator.GetBytes(500), Passphrase));
	}

	[Fact]
	public void A_file_too_large_to_be_a_backup_is_not_read()
	{
		Assert.Null(BackupFile.Open(new byte[BackupFile.MaximumFileBytes + 1], Passphrase));
	}

	[Fact]
	public void A_version_this_build_does_not_read_does_not_open()
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), 3);

		Assert.Null(BackupFile.Open(file, Passphrase));
	}

	[Theory]
	[InlineData(599_999u)]
	[InlineData(0u)]
	[InlineData(10_000_001u)]
	[InlineData(uint.MaxValue)]
	public void Iterations_outside_the_limits_are_refused_before_deriving_anything(uint iterations)
	{
		var file = BackupFile.Seal(Contents(), Passphrase);

		BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(6), iterations);

		Assert.Null(BackupFile.Open(file, Passphrase));
	}

	[Fact]
	public void Well_sealed_nonsense_does_not_open()
	{
		// The right key and a good tag, around something Seal never wrote.
		Assert.Null(BackupFile.Open(BackupBytes.Seal("not gzip at all"u8.ToArray(), Passphrase), Passphrase));
		Assert.Null(BackupFile.Open(BackupBytes.Seal(BackupBytes.Gzip("not json"u8.ToArray()), Passphrase), Passphrase));
	}

	[Theory]
	[InlineData("""{"formatVersion":2,"memberId":7,"history":{}}""")] // Disagrees with the header
	[InlineData("""{"formatVersion":1,"memberId":0,"history":{}}""")] // Nobody's
	[InlineData("""{"formatVersion":1,"memberId":7}""")] // No history
	[InlineData("""{"formatVersion":1,"memberId":7,"history":{"messages":null}}""")]
	public void Sealed_contents_that_seal_could_not_have_written_do_not_open(string json)
	{
		var file = BackupBytes.Seal(BackupBytes.Gzip(Encoding.UTF8.GetBytes(json)), Passphrase);

		Assert.Null(BackupFile.Open(file, Passphrase));
	}

	[Fact]
	public void The_device_comes_back_when_the_passphrase_opens_it()
	{
		var key = Sealing.NewKeypair();
		var file = BackupFile.Seal(Contents() with { Device = new BackupDevice { Id = "d_abc", PrivateKey = key.PrivateKeyPem } }, Passphrase);

		var device = BackupFile.Open(file, Passphrase)!.Device!;

		Assert.Equal("d_abc", device.Id);
		Assert.Equal(key.PrivateKeyPem, device.PrivateKey);
		Assert.Equal(key.PublicKey, device.PublicKey());
	}

	[Fact]
	public void The_key_is_not_readable_in_the_file()
	{
		var key = Sealing.NewKeypair();
		var file = BackupFile.Seal(Contents() with { Device = new BackupDevice { Id = "d_abc", PrivateKey = key.PrivateKeyPem } }, Passphrase);

		var everything = Encoding.Latin1.GetString(file);

		Assert.DoesNotContain("PRIVATE KEY", everything, StringComparison.Ordinal);
		Assert.DoesNotContain("d_abc", everything, StringComparison.Ordinal);
	}

	[Fact]
	public void Printing_a_backup_does_not_print_the_key()
	{
		var contents = Contents() with { Device = new BackupDevice { Id = "d_abc", PrivateKey = "-----BEGIN PRIVATE KEY-----" } };

		Assert.DoesNotContain("PRIVATE KEY", contents.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("PRIVATE KEY", contents.Device.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""{"id":"d_abc"}""")]
	[InlineData("""{"privateKey":"key"}""")]
	[InlineData("""{"id":"","privateKey":""}""")]
	public void A_device_that_is_not_whole_is_no_device(string device)
	{
		var json = $$"""{"formatVersion":2,"memberId":7,"history":{"messages":[],"sent":[],"deleted":[]},"device":{{device}}}""";
		var file = BackupBytes.Seal(BackupBytes.Gzip(Encoding.UTF8.GetBytes(json)), Passphrase, version: 2);

		var opened = BackupFile.Open(file, Passphrase);

		Assert.NotNull(opened);
		Assert.Null(opened.Device);
	}

	[Fact]
	public void A_version_1_file_never_has_a_device_whatever_it_says()
	{
		const string json = """{"formatVersion":1,"memberId":7,"history":{"messages":[],"sent":[],"deleted":[]},"device":{"id":"d_abc","privateKey":"key"}}""";
		var file = BackupBytes.Seal(BackupBytes.Gzip(Encoding.UTF8.GetBytes(json)), Passphrase);

		Assert.Null(BackupFile.Open(file, Passphrase)!.Device);
	}

	[Fact]
	public void A_version_1_file_saying_it_is_version_2_does_not_open()
	{
		const string json = """{"formatVersion":1,"memberId":7,"history":{"messages":[],"sent":[],"deleted":[]}}""";
		var file = BackupBytes.Seal(BackupBytes.Gzip(Encoding.UTF8.GetBytes(json)), Passphrase, version: 2);

		Assert.Null(BackupFile.Open(file, Passphrase));
	}

	[Fact]
	public void A_key_that_cannot_be_read_has_no_public_half()
	{
		Assert.Empty(new BackupDevice { Id = "d_abc", PrivateKey = "not a key" }.PublicKey());
	}

	[Fact]
	public void A_sealed_file_that_inflates_beyond_any_backup_does_not_open()
	{
		var bomb = BackupBytes.Gzip(new byte[257 * 1024 * 1024]);

		Assert.Null(BackupFile.Open(BackupBytes.Seal(bomb, Passphrase), Passphrase));
	}

	[Fact]
	public void A_short_passphrase_is_refused_at_sealing()
	{
		Assert.Throws<ArgumentException>(() => BackupFile.Seal(Contents(), "elevenchars"));
	}

	[Theory]
	[InlineData("elevenchars", "elevenchars", "at least 12")]
	[InlineData("", "", "at least 12")]
	[InlineData(null, null, "at least 12")]
	[InlineData("           x", "           x", "at least 12")] // Eleven spaces do not count.
	[InlineData("twelve chars", "twelve charz", "do not match")]
	[InlineData("twelve chars", null, "do not match")]
	public void A_passphrase_problem_is_said_in_words(string? first, string? second, string words)
	{
		Assert.Contains(words, BackupFile.PassphraseProblem(first, second), StringComparison.Ordinal);
	}

	[Fact]
	public void Twelve_characters_typed_twice_will_do()
	{
		Assert.Null(BackupFile.PassphraseProblem("twelve chars", "twelve chars"));
	}

	[Fact]
	public void A_space_the_keyboard_added_does_not_lock_the_member_out()
	{
		var file = BackupFile.Seal(Contents(), Passphrase + " ");

		Assert.NotNull(BackupFile.Open(file, " " + Passphrase));
	}

	[Fact]
	public void The_same_accented_passphrase_opens_however_it_was_spelled()
	{
		// iOS and Android keyboards can give either of these for the same keys.
		const string composed = "café at half past nine";
		const string decomposed = "café at half past nine";

		Assert.NotNull(BackupFile.Open(BackupFile.Seal(Contents(), composed), decomposed));
	}

	[Fact]
	public void Half_a_surrogate_pair_does_not_throw()
	{
		const string broken = "a long passphrase \uD800";

		Assert.NotNull(BackupFile.Open(BackupFile.Seal(Contents(), broken), broken));
	}

	[Fact]
	public void The_file_name_is_the_date_on_this_phones_calendar()
	{
		var at = new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero);

		Assert.Equal("link-backup-2026-11-05.linkbackup", BackupFile.FileNameFor(at));
	}

	private static BackupContents Contents() => new()
	{
		MemberId = 7,
		CreatedAt = 1_790_000_000,
		History = new HistorySnapshot
		{
			Messages = [new LinkMessage { Id = 12, Subject = "Train times", Body = "Meet at nine" }],
			Sent = [new SentMessage { Id = 13, To = "Jo B", Subject = "Thanks" }],
			Deleted = [new DeletedMessage(11, 10)],
			ClearedUpTo = 9,
			SentClearedUpTo = 8,
			PolledUpTo = 13,
		},
		Settings = new BackupSettings
		{
			Style = (int)LinkStyle.SiloTerminal,
			Sound = false,
			OnlyRegistered = true,
			PullToRefresh = true,
		},
	};
}

/// <summary>
/// The file's layout, written out a second time, without calling the code
/// under test.
/// </summary>
internal sealed record BackupBytes(byte[] File, int Iterations, byte[] Salt, byte[] Nonce, byte[] Tag, byte[] Ciphertext)
{
	private const int HeaderBytes = 38;

	public static BackupBytes Read(byte[] file) => new(
		file,
		BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(6)),
		file[10..26],
		file[26..38],
		file[38..54],
		file[54..]);

	/// <summary>Open a file by hand and inflate what is inside.</summary>
	public byte[] Plaintext(string passphrase)
	{
		var plaintext = new byte[Ciphertext.Length];

		using (var gcm = new AesGcm(Key(passphrase, Salt, Iterations), 16))
		{
			gcm.Decrypt(Nonce, Ciphertext, Tag, plaintext, File[..HeaderBytes]);
		}

		using var source = new MemoryStream(plaintext);
		using var gzip = new GZipStream(source, CompressionMode.Decompress);
		using var output = new MemoryStream();

		gzip.CopyTo(output);

		return output.ToArray();
	}

	/// <summary>The same, leaving the header out of the authentication.</summary>
	public void PlaintextWithoutHeader(string passphrase)
	{
		using var gcm = new AesGcm(Key(passphrase, Salt, Iterations), 16);

		gcm.Decrypt(Nonce, Ciphertext, Tag, new byte[Ciphertext.Length]);
	}

	/// <summary>Seal anything at all, correctly, as somebody holding the passphrase could.</summary>
	public static byte[] Seal(byte[] plaintext, string passphrase, ushort version = 1)
	{
		const int iterations = 600_000;

		var file = new byte[54 + plaintext.Length];

		"LKBK"u8.CopyTo(file);
		BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), version);
		BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(6), iterations);
		RandomNumberGenerator.Fill(file.AsSpan(10, 28));

		using var gcm = new AesGcm(Key(passphrase, file[10..26], iterations), 16);

		gcm.Encrypt(file.AsSpan(26, 12), plaintext, file.AsSpan(54), file.AsSpan(38, 16), file.AsSpan(0, HeaderBytes));

		return file;
	}

	public static byte[] Gzip(byte[] content)
	{
		using var output = new MemoryStream();

		using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
		{
			gzip.Write(content);
		}

		return output.ToArray();
	}

	private static byte[] Key(string passphrase, byte[] salt, int iterations) =>
		Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
}
