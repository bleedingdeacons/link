using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// Making and restoring a backup over a real history file: who may restore
/// what, that a refusal changes nothing, and that nothing that signs in ever
/// reaches the file.
/// </summary>
public sealed class MessageBackupTests : IDisposable
{
	private const string Passphrase = "a long enough passphrase";
	private const string Token = "device-token-that-must-never-leave";

	private readonly string _directory = Path.Combine(Path.GetTempPath(), "link-tests-" + Guid.NewGuid().ToString("N"));
	private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
	private readonly MemorySessions _sessions = new() { Session = new DeviceSession { Token = Token, DeviceId = 3, MemberId = 7, MemberName = "Dave B" } };
	private readonly MemorySettings _settings = new();
	private readonly JsonMessageHistory _history;

	public MessageBackupTests()
	{
		_history = new JsonMessageHistory(Path.Combine(_directory, "messages.bin"), _key);
	}

	public void Dispose()
	{
		_history.Dispose();

		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	private MessageBackup Subject => new(_sessions, _history, _settings, _settings, _settings, _settings);

	[Fact]
	public async Task A_backup_restores_onto_a_phone_that_has_lost_everything()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync([new LinkMessage { Id = 12, Subject = "Train times" }]);
		await _history.SaveSentAsync(new SentMessage { Id = 13, Subject = "Thanks", To = "Jo B" });
		await _history.MarkPolledAsync(12);

		var made = await Subject.MakeAsync(Passphrase);

		await _history.ResetAsync();
		await _history.AdoptAsync(7);
		Assert.Empty(await _history.AllAsync());

		var restored = await Subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(BackupRestoreOutcome.Restored, restored.Outcome);
		Assert.Equal(2, restored.Added);
		Assert.Equal(0, restored.StayedGone);
		Assert.Equal("Train times", Assert.Single(await _history.AllAsync()).Subject);
		Assert.Equal("Jo B", Assert.Single(await _history.SentAsync()).To);
		Assert.Equal(12, await _history.PollFromAsync());
		Assert.Contains("2 messages", restored.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task The_file_is_named_and_stamped_for_the_day_and_the_member()
	{
		await _history.AdoptAsync(7);

		var before = DateTimeOffset.UtcNow.AddSeconds(-1);
		var made = await Subject.MakeAsync(Passphrase);

		Assert.NotNull(made);
		Assert.Equal(7, made.MemberId);
		Assert.InRange(made.At, before, DateTimeOffset.UtcNow.AddSeconds(1));
		Assert.Equal(BackupFile.FileNameFor(made.At.ToLocalTime()), made.FileName);

		var restored = await Subject.RestoreAsync(made.File, Passphrase);

		Assert.Equal(made.At.ToUnixTimeSeconds(), restored.MadeAt!.Value.ToUnixTimeSeconds());
	}

	[Fact]
	public async Task A_wrong_passphrase_and_a_damaged_file_say_the_same_thing_and_change_nothing()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync([new LinkMessage { Id = 12, Subject = "Train times" }]);

		var made = await Subject.MakeAsync(Passphrase);

		await _history.SaveAsync([new LinkMessage { Id = 14, Subject = "Newer" }]);
		_settings.Style = LinkStyle.StarshipConsole;

		var damaged = (byte[])made!.File.Clone();
		damaged[^1] ^= 0x01;

		var header = (byte[])made.File.Clone();
		header[12] ^= 0x01;

		var wrong = await Subject.RestoreAsync(made.File, "not the passphrase");
		var cut = await Subject.RestoreAsync(damaged, Passphrase);
		var tampered = await Subject.RestoreAsync(header, Passphrase);

		foreach (var result in new[] { wrong, cut, tampered })
		{
			Assert.Equal(BackupRestoreOutcome.CouldNotOpen, result.Outcome);
			Assert.Equal(MessageBackup.CouldNotOpen, result.Message);
			Assert.Null(result.Settings);
		}

		Assert.Equal([14L, 12L], (await _history.AllAsync()).Select(m => m.Id));
		Assert.Equal(LinkStyle.StarshipConsole, _settings.Style);
	}

	[Fact]
	public async Task Another_members_backup_is_refused_and_changes_nothing()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync([new LinkMessage { Id = 12, Subject = "Theirs" }]);

		var made = await Subject.MakeAsync(Passphrase);

		_sessions.Session = new DeviceSession { Token = "another-token", MemberId = 9 };
		await _history.AdoptAsync(9);
		await _history.SaveAsync([new LinkMessage { Id = 20, Subject = "Mine" }]);

		var restored = await Subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(BackupRestoreOutcome.OtherMember, restored.Outcome);
		Assert.Equal(MessageBackup.OtherMember, restored.Message);
		Assert.Null(restored.Settings);
		Assert.Equal("Mine", Assert.Single(await _history.AllAsync()).Subject);
	}

	[Fact]
	public async Task A_store_held_for_somebody_else_refuses_the_backup_too()
	{
		// The session and the store disagree, which should never happen. The
		// session is a convenience rather than an authority, so the store
		// keeps its own check.
		await _history.AdoptAsync(9);
		await _history.SaveAsync([new LinkMessage { Id = 20, Subject = "Nine's" }]);

		var file = BackupFile.Seal(
			new BackupContents { MemberId = 7, History = new HistorySnapshot { Messages = [new LinkMessage { Id = 12 }] } },
			Passphrase);

		var restored = await Subject.RestoreAsync(file, Passphrase);

		Assert.Equal(BackupRestoreOutcome.OtherMember, restored.Outcome);
		Assert.Equal("Nine's", Assert.Single(await _history.AllAsync()).Subject);
	}

	[Fact]
	public async Task A_store_held_for_somebody_else_is_not_backed_up()
	{
		await _history.AdoptAsync(9);
		await _history.SaveAsync([new LinkMessage { Id = 20, Subject = "Nine's" }]);

		Assert.Null(await Subject.MakeAsync(Passphrase));
	}

	[Fact]
	public async Task A_store_nobody_has_claimed_is_backed_up_for_the_member_signed_in()
	{
		await _history.SaveAsync([new LinkMessage { Id = 12, Subject = "From before owners" }]);

		var made = await Subject.MakeAsync(Passphrase);

		Assert.Equal(7, BackupFile.Open(made!.File, Passphrase)!.MemberId);
	}

	[Fact]
	public async Task Nobody_signed_in_can_neither_back_up_nor_restore()
	{
		await _history.AdoptAsync(7);
		var made = await Subject.MakeAsync(Passphrase);

		_sessions.Session = null;

		Assert.Null(await Subject.MakeAsync(Passphrase));

		var restored = await Subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(BackupRestoreOutcome.NotSignedIn, restored.Outcome);
		Assert.Equal(MessageBackup.NotSignedIn, restored.Message);
	}

	[Fact]
	public async Task A_store_that_cannot_be_written_says_so()
	{
		await _history.AdoptAsync(7);
		var made = await Subject.MakeAsync(Passphrase);

		// The folder is in the way of the file being moved into place, so the
		// write fails however this platform treats permissions.
		var file = Path.Combine(_directory, "blocked", "messages.bin");
		using var history = new JsonMessageHistory(file, _key);
		Directory.CreateDirectory(file);

		var subject = new MessageBackup(_sessions, history, _settings, _settings, _settings, _settings);
		var restored = await subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(BackupRestoreOutcome.NotWritten, restored.Outcome);
		Assert.Equal(MessageBackup.NotWritten, restored.Message);
	}

	[Fact]
	public async Task The_settings_come_back_only_when_they_are_applied()
	{
		await _history.AdoptAsync(7);

		_settings.Style = LinkStyle.SiloTerminal;
		_settings.Sound = false;
		_settings.OnlyRegistered = false;
		_settings.Pull = true;

		var made = await Subject.MakeAsync(Passphrase);

		_settings.Style = LinkStyle.LinkBlue;
		_settings.Sound = true;
		_settings.OnlyRegistered = true;
		_settings.Pull = false;

		var restored = await Subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(LinkStyle.LinkBlue, _settings.Style);

		Subject.ApplySettings(restored.Settings);

		Assert.Equal(LinkStyle.SiloTerminal, _settings.Style);
		Assert.False(_settings.Sound);
		Assert.False(_settings.OnlyRegistered);
		Assert.True(_settings.Pull);
	}

	[Fact]
	public void A_style_this_build_does_not_know_leaves_the_current_one()
	{
		_settings.Style = LinkStyle.IntergroupGreen;

		Subject.ApplySettings(new BackupSettings { Style = 99, Sound = false });

		Assert.Equal(LinkStyle.IntergroupGreen, _settings.Style);
		Assert.False(_settings.Sound);
	}

	[Fact]
	public void A_setting_the_backup_does_not_carry_is_left_alone()
	{
		_settings.Style = LinkStyle.IntergroupGreen;
		_settings.Sound = false;

		Subject.ApplySettings(new BackupSettings());
		Subject.ApplySettings(null);

		Assert.Equal(LinkStyle.IntergroupGreen, _settings.Style);
		Assert.False(_settings.Sound);
	}

	[Fact]
	public async Task No_token_and_no_key_ever_reach_the_file()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync(
		[
			new LinkMessage { Id = 11, Subject = "To delete" },
			new LinkMessage { Id = 12, Subject = "Train times", Body = "Meet at nine" },
		]);
		await _history.SaveSentAsync(new SentMessage { Id = 13, Subject = "Thanks", To = "Jo B" });
		await _history.DeleteAsync(11);

		var made = await Subject.MakeAsync(Passphrase);

		var json = Encoding.UTF8.GetString(BackupBytes.Read(made!.File).Plaintext(Passphrase));

		Assert.DoesNotContain(Token, json, StringComparison.Ordinal);
		Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.OrdinalIgnoreCase);

		// The check that catches next year's field, not just today's values.
		using var document = JsonDocument.Parse(json);
		var names = new SortedSet<string>(StringComparer.Ordinal);
		Collect(document.RootElement, names);

		Assert.DoesNotContain(names, n => n.Contains("token", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("key", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("password", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("secret", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("device", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));

		Assert.Equal(AllowedNames, names);
	}

	[Fact]
	public void Settings_says_when_the_last_backup_was_or_that_there_is_none()
	{
		var at = new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero);

		Assert.Equal("Last backup made on 05/11/2026", Support.LastBackup.Describe(at));
		Assert.Equal("No backup made on this phone yet", Support.LastBackup.Describe(null));
	}

	[Fact]
	public void The_member_is_told_what_a_backup_is_and_what_a_passphrase_costs()
	{
		Assert.Contains("outside Link", MessageBackup.OutsideLink, StringComparison.Ordinal);
		Assert.Contains("Clearing messages, signing out and Remove this phone", MessageBackup.OutsideLink, StringComparison.Ordinal);
		Assert.Contains("erasure request cannot reach it", MessageBackup.OutsideLink, StringComparison.Ordinal);
		Assert.Contains("nobody can recover it", MessageBackup.ForgottenPassphrase, StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_v1_backup_still_restores()
	{
		// Made once by BackupFile at format version 1 and committed, and it
		// is not to be regenerated: the point of it is that the bytes are old.
		// See Fixtures/backup-v1.md.
		var file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "backup-v1.linkbackup"));

		await _history.AdoptAsync(7);

		var restored = await Subject.RestoreAsync(file, "fixture passphrase, test messages only");

		Assert.Equal(BackupRestoreOutcome.Restored, restored.Outcome);
		Assert.Equal(3, restored.Added);

		var messages = await _history.AllAsync();
		Assert.Equal([12L, 11L], messages.Select(m => m.Id));
		Assert.Equal("Test message, sent to nobody", messages[1].Subject);
		Assert.Equal(10, messages[0].ReplyToId);
		Assert.True(messages[1].IsRead);
		Assert.True(messages[1].Acknowledged);

		var sent = Assert.Single(await _history.SentAsync());
		Assert.Equal(13, sent.Id);
		Assert.Equal("Test member", sent.To);
		Assert.Equal(1, sent.Recipients);

		Assert.Equal(new DeletedMessage(10, 0), Assert.Single(await _history.DeletedAsync()));
		Assert.Equal(13, await _history.PollFromAsync());

		Assert.Equal(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), restored.MadeAt);
		Assert.Equal((int)LinkStyle.IntergroupGreen, restored.Settings!.Style);
		Assert.False(restored.Settings.Sound);
		Assert.False(restored.Settings.OnlyRegistered);
		Assert.True(restored.Settings.PullToRefresh);
	}

	[Fact]
	public async Task The_v1_fixture_holds_no_key_and_no_token()
	{
		var file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "backup-v1.linkbackup"));

		var json = Encoding.UTF8.GetString(BackupBytes.Read(file).Plaintext("fixture passphrase, test messages only"));

		using var document = JsonDocument.Parse(json);
		var names = new SortedSet<string>(StringComparer.Ordinal);
		Collect(document.RootElement, names);

		Assert.Equal(AllowedNames, names);
		Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Every property name a version 1 backup may hold. A new one fails this
	/// test, so that it goes in on purpose or not at all.
	/// </summary>
	private static readonly SortedSet<string> AllowedNames = new(
		[
			"formatVersion", "memberId", "createdAt", "history", "settings",

			// History.
			"messages", "sent", "deleted", "clearedUpTo", "sentClearedUpTo", "polledUpTo",

			// A received message, and what is computed from it.
			"id", "uuid", "subject", "body", "sender", "senderId", "replyToId", "readAt", "isRead", "acknowledged", "sentLocal",

			// A sent copy, and what is computed from it.
			"to", "recipients", "received", "read", "state", "settled", "isReceived", "summary",

			// Settings.
			"style", "sound", "onlyRegistered", "pullToRefresh",
		],
		StringComparer.Ordinal);

	private static void Collect(JsonElement element, SortedSet<string> names)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					names.Add(property.Name);
					Collect(property.Value, names);
				}

				break;

			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					Collect(item, names);
				}

				break;
		}
	}

	private sealed class MemorySessions : ISessionStore
	{
		public DeviceSession? Session { get; set; }

		public Task<DeviceSession?> LoadAsync() => Task.FromResult(Session);

		public Task SaveAsync(DeviceSession session)
		{
			Session = session;

			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			Session = null;

			return Task.CompletedTask;
		}
	}

	private sealed class MemorySettings : IThemeChoice, IArrivalSound, IRecipientListing, IPullToRefresh
	{
		public LinkStyle Style { get; set; }

		public bool Sound { get; set; } = true;

		public bool OnlyRegistered { get; set; } = true;

		public bool Pull { get; set; }

		bool IArrivalSound.Enabled
		{
			get => Sound;
			set => Sound = value;
		}

		bool IPullToRefresh.Enabled
		{
			get => Pull;
			set => Pull = value;
		}

		public void Play()
		{
		}
	}
}
