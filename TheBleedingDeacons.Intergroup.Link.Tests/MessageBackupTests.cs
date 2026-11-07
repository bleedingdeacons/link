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
	private readonly MemoryKeys _keys = new();
	private readonly Sealing.Keypair _oldPhone = Sealing.NewKeypair();
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

	private MessageBackup Subject => new(_sessions, _history, _settings, _settings, _settings, _settings, _keys);

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

		var subject = new MessageBackup(_sessions, history, _settings, _settings, _settings, _settings, _keys);
		var restored = await subject.RestoreAsync(made!.File, Passphrase);

		Assert.Equal(BackupRestoreOutcome.NotWritten, restored.Outcome);
		Assert.Equal(MessageBackup.NotWritten, restored.Message);
	}

	[Fact]
	public async Task A_backup_says_which_device_it_came_from_and_holds_its_key()
	{
		await _history.AdoptAsync(7);
		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_old" });

		var made = await Subject.MakeAsync(Passphrase);

		var device = BackupFile.Open(made!.File, Passphrase)!.Device!;
		Assert.Equal("d_old", device.Id);
		Assert.Equal(_keys.Pair.PrivateKeyPem, device.PrivateKey);
	}

	[Fact]
	public async Task A_phone_with_no_key_makes_a_backup_with_no_device()
	{
		await _history.AdoptAsync(7);
		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_old" });
		await _keys.ClearAsync();

		var made = await Subject.MakeAsync(Passphrase);

		Assert.Null(BackupFile.Open(made!.File, Passphrase)!.Device);
	}

	[Fact]
	public async Task Restoring_in_settings_on_another_phone_says_that_phone_stays()
	{
		var file = await BackupFromAnotherPhoneAsync();

		var restored = await Subject.RestoreAsync(file, Passphrase);

		Assert.True(restored.Succeeded);
		Assert.EndsWith(MessageBackup.OtherPhoneStays, restored.Message, StringComparison.Ordinal);
		Assert.Equal("d_new", (await _sessions.LoadAsync())!.FirebaseDeviceId);
	}

	[Fact]
	public async Task Restoring_in_settings_on_the_same_phone_says_nothing_about_another()
	{
		await _history.AdoptAsync(7);
		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_old" });
		var made = await Subject.MakeAsync(Passphrase);

		var restored = await Subject.RestoreAsync(made!.File, Passphrase);

		Assert.DoesNotContain(MessageBackup.OtherPhoneStays, restored.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Restoring_in_settings_leaves_this_phones_key_alone()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var before = _keys.Pair.PrivateKeyPem;

		await Subject.RestoreAsync(file, Passphrase);

		Assert.Equal(before, _keys.Pair.PrivateKeyPem);
	}

	[Fact]
	public async Task A_held_backup_changes_nothing_until_the_sign_in()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var before = _keys.Pair.PrivateKeyPem;
		var subject = Subject;

		var held = await subject.HoldForSignInAsync(file, Passphrase);

		Assert.Equal(BackupRestoreOutcome.Held, held.Outcome);
		Assert.Equal(MessageBackup.WillTakeOver, held.Message);
		Assert.True(subject.IsHolding);
		Assert.False(held.Succeeded);
		Assert.Empty(await _history.AllAsync());
		Assert.Equal(before, _keys.Pair.PrivateKeyPem);
	}

	[Fact]
	public async Task A_wrong_passphrase_holds_nothing()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var subject = Subject;

		var held = await subject.HoldForSignInAsync(file, "not the passphrase at all");

		Assert.Equal(BackupRestoreOutcome.CouldNotOpen, held.Outcome);
		Assert.False(subject.IsHolding);
	}

	[Fact]
	public async Task Signing_in_is_aimed_at_the_backups_device_with_the_backups_key()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		var aimed = subject.AimedAtHeldDevice(new EnrolmentRequest { PublicKey = "this-phones-new-key", Platform = "android" });

		Assert.Equal("d_old", aimed.TakeOverDeviceId);
		Assert.Equal(_oldPhone.PublicKey, aimed.TakeOverPublicKey);
		Assert.Equal("this-phones-new-key", aimed.PublicKey);
	}

	[Fact]
	public async Task With_nothing_held_an_enrolment_is_left_as_it_was()
	{
		var request = new EnrolmentRequest { PublicKey = "key", Platform = "android" };

		Assert.Same(request, Subject.AimedAtHeldDevice(request));
		Assert.Null(await Subject.FinishHeldAsync(tookOver: false));
	}

	[Fact]
	public async Task A_backup_with_no_device_is_not_aimed_at_anything()
	{
		await _history.AdoptAsync(7);
		var made = await Subject.MakeAsync(Passphrase);
		var subject = Subject;

		var held = await subject.HoldForSignInAsync(made!.File, Passphrase);

		Assert.Equal(MessageBackup.WillJoinAsNew, held.Message);
		Assert.Empty(subject.AimedAtHeldDevice(new EnrolmentRequest { PublicKey = "key", Platform = "android" }).TakeOverDeviceId);
	}

	[Fact]
	public async Task A_takeover_keeps_the_backups_key_and_restores_the_messages()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		await subject.KeepHeldKeyAsync();
		var restored = await subject.FinishHeldAsync(tookOver: true);

		Assert.Equal(_oldPhone.PrivateKeyPem, _keys.Pair.PrivateKeyPem);
		Assert.True(restored!.Succeeded);
		Assert.StartsWith(MessageBackup.TookOver, restored.Message, StringComparison.Ordinal);
		Assert.Equal([12L], (await _history.AllAsync()).Select(m => m.Id));
		Assert.False(subject.IsHolding);
	}

	[Fact]
	public async Task The_first_sync_after_a_takeover_starts_from_the_backups_mark()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		await subject.FinishHeldAsync(tookOver: true);

		Assert.Equal(12, await _history.PollFromAsync());
	}

	[Fact]
	public async Task A_refused_takeover_still_restores_the_messages_and_keeps_the_new_key()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var before = _keys.Pair.PrivateKeyPem;
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		var restored = await subject.FinishHeldAsync(tookOver: false);

		Assert.Equal(before, _keys.Pair.PrivateKeyPem);
		Assert.True(restored!.Succeeded);
		Assert.StartsWith(MessageBackup.CouldNotTakeOver, restored.Message, StringComparison.Ordinal);
		Assert.Equal([12L], (await _history.AllAsync()).Select(m => m.Id));
	}

	[Fact]
	public async Task A_held_backup_from_another_member_is_refused_after_the_sign_in()
	{
		var file = await BackupFromAnotherPhoneAsync();
		await _sessions.SaveAsync(_sessions.Session! with { MemberId = 9 });
		await _history.AdoptAsync(9);
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		var restored = await subject.FinishHeldAsync(tookOver: false);

		Assert.Equal(BackupRestoreOutcome.OtherMember, restored!.Outcome);
		Assert.Empty(await _history.AllAsync());
	}

	[Fact]
	public async Task A_held_backup_can_be_put_down()
	{
		var file = await BackupFromAnotherPhoneAsync();
		var subject = Subject;
		await subject.HoldForSignInAsync(file, Passphrase);

		subject.ForgetHeld();

		Assert.False(subject.IsHolding);
		Assert.Empty(subject.AimedAtHeldDevice(new EnrolmentRequest { PublicKey = "key", Platform = "android" }).TakeOverDeviceId);
	}

	[Fact]
	public async Task A_v1_backup_can_be_held_and_joins_as_new()
	{
		var file = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "backup-v1.linkbackup"));
		await _history.AdoptAsync(7);
		var subject = Subject;

		var held = await subject.HoldForSignInAsync(file, "fixture passphrase, test messages only");
		var restored = await subject.FinishHeldAsync(tookOver: false);

		Assert.Equal(MessageBackup.WillJoinAsNew, held.Message);
		Assert.StartsWith(MessageBackup.JoinedAsNew, restored!.Message, StringComparison.Ordinal);
		Assert.Equal(3, restored.Added);
	}

	/// <summary>
	/// A backup made on the phone before this one, then this phone signed in as a new
	/// device with a new key and an empty history.
	/// </summary>
	private async Task<byte[]> BackupFromAnotherPhoneAsync()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync([new LinkMessage { Id = 12, Subject = "Train times", Body = "Meet at nine" }]);
		await _history.MarkPolledAsync(12);
		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_old" });
		await _keys.ImportAsync(_oldPhone.PrivateKeyPem);

		var made = await Subject.MakeAsync(Passphrase);

		await _history.ResetAsync();
		await _history.AdoptAsync(7);
		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_new" });
		await _keys.RegenerateAsync();

		return made!.File;
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
	public async Task No_token_ever_reaches_the_file_and_the_only_key_is_the_devices()
	{
		await _history.AdoptAsync(7);
		await _history.SaveAsync(
		[
			new LinkMessage { Id = 11, Subject = "To delete" },
			new LinkMessage { Id = 12, Subject = "Train times", Body = "Meet at nine" },
		]);
		await _history.SaveSentAsync(new SentMessage { Id = 13, Subject = "Thanks", To = "Jo B" });
		await _history.DeleteAsync(11);

		await _sessions.SaveAsync(_sessions.Session! with { FirebaseDeviceId = "d_abc" });

		var made = await Subject.MakeAsync(Passphrase);

		var json = Encoding.UTF8.GetString(BackupBytes.Read(made!.File).Plaintext(Passphrase));

		Assert.DoesNotContain(Token, json, StringComparison.Ordinal);

		// The check that catches next year's field, not just today's values.
		using var document = JsonDocument.Parse(json);
		var names = new SortedSet<string>(StringComparer.Ordinal);
		Collect(document.RootElement, names);

		Assert.DoesNotContain(names, n => n.Contains("token", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("password", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("secret", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(names, n => n.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));

		// Version 2's two names and nothing else: the device, and its key.
		Assert.Equal(new SortedSet<string>(AllowedNames.Concat(["device", "privateKey"]), StringComparer.Ordinal), names);
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

	private sealed class MemoryKeys : IDeviceKeyStore
	{
		public Sealing.Keypair Pair { get; private set; } = Sealing.NewKeypair();

		private bool _cleared;

		public Task<bool> HasKeyAsync() => Task.FromResult(!_cleared);

		public Task<string> RegenerateAsync()
		{
			Pair = Sealing.NewKeypair();
			_cleared = false;

			return Task.FromResult(Pair.PublicKey);
		}

		public Task<string> ImportAsync(string privateKeyPem)
		{
			Pair = new Sealing.Keypair(new BackupDevice { PrivateKey = privateKeyPem }.PublicKey(), privateKeyPem);
			_cleared = false;

			return Task.FromResult(Pair.PublicKey);
		}

		public Task<string> PublicKeyAsync() => Task.FromResult(_cleared ? string.Empty : Pair.PublicKey);

		public Task<string> PrivateKeyAsync() => Task.FromResult(_cleared ? string.Empty : Pair.PrivateKeyPem);

		public Task ClearAsync()
		{
			_cleared = true;

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
