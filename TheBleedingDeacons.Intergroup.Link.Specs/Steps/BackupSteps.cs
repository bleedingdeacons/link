using System.Text.Json;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Steps;

/// <summary>
/// Making a backup and restoring one, on <see cref="MessageBackup"/>, which is
/// what Settings calls. The share sheet, the picker and the fingerprint sheet
/// are the phone's, and are in OnDevice.feature.
/// </summary>
[Binding]
public sealed class BackupSteps(World world)
{
	private const string Passphrase = "a long enough passphrase";

	private byte[]? _backup;
	private BackupRestore? _restored;

	private byte[] Backup => _backup.ShouldNotBeNull();

	private BackupRestore Restored => _restored.ShouldNotBeNull();

	[Given(@"^a backup is made with the passphrase ""(.+)""$")]
	[When(@"^a backup is made with the passphrase ""(.+)""$")]
	public async Task Made(string passphrase) =>
		_backup = (await world.Backup.MakeAsync(passphrase)).ShouldNotBeNull().File;

	[When(@"^the backup is restored with the passphrase ""(.+)""$")]
	public async Task Restores(string passphrase) =>
		_restored = await world.Backup.RestoreAsync(Backup, passphrase);

	/// <summary>The offsets are the layout in <see cref="BackupFile"/>, on purpose written out again here.</summary>
	[When(@"^one byte of the backup's (version|iterations|salt|nonce|tag|contents) is changed$")]
	public void Tampers(string part) => Backup[part switch
	{
		"version" => 5,
		"iterations" => 9,
		"salt" => 10,
		"nonce" => 26,
		"tag" => 38,
		_ => 54,
	}] ^= 0x01;

	[When(@"^the backup is cut short$")]
	public void CutsShort() => _backup = Backup[..^4];

	[When(@"^the member moves to a new phone$")]
	public void NewPhone() => world.NewPhone();

	[When(@"^Fellowship has swept its messages$")]
	public void Swept() => world.Fellowship.Stored.Clear();

	[Given(@"^the phone is drawn in (\w+) with the sound (on|off)$")]
	[When(@"^the phone is changed to (\w+) with the sound (on|off)$")]
	public void Choose(LinkStyle style, string sound)
	{
		world.Choices.Style = style;
		world.Choices.Sound = sound == "on";
	}

	[When(@"^the restored choices are applied$")]
	public void Applies() => world.Backup.ApplySettings(Restored.Settings);

	[Then(@"^the phone is drawn in (\w+) with the sound (on|off)$")]
	public void IsChosen(LinkStyle style, string sound)
	{
		world.Choices.Style.ShouldBe(style);
		world.Choices.Sound.ShouldBe(sound == "on");
	}

	[Then(@"^the backup is restored$")]
	public void IsRestored() => Restored.Outcome.ShouldBe(BackupRestoreOutcome.Restored);

	[Then(@"^the backup could not be opened$")]
	public void CouldNotOpen() => Restored.Outcome.ShouldBe(BackupRestoreOutcome.CouldNotOpen);

	[Then(@"^the backup belongs to another member$")]
	public void BelongsElsewhere() => Restored.Outcome.ShouldBe(BackupRestoreOutcome.OtherMember);

	[Then(@"^restoring says ""(.+)""$")]
	public void Says(string words) => Restored.Message.ShouldContain(words);

	[Then(@"^the restore added (\d+) messages?$")]
	public void Added(int count) => Restored.Added.ShouldBe(count);

	[Given(@"^this phone is device ""(.+)"" on Firebase$")]
	public async Task OnFirebase(string deviceId) =>
		await world.Sessions.SaveAsync(world.Sessions.Session.ShouldNotBeNull() with { FirebaseDeviceId = deviceId });

	/// <summary>Only once the passphrase has opened it: the bytes are read by hand in Link.Tests.</summary>
	[Then(@"^the backup holds device ""(.+)"" and this phone's key$")]
	public async Task HoldsTheDevice(string deviceId)
	{
		var device = BackupFile.Open(Backup, Passphrase).ShouldNotBeNull().Device.ShouldNotBeNull();

		device.Id.ShouldBe(deviceId);
		device.PrivateKey.ShouldBe(await world.Keys.PrivateKeyAsync());
		device.PublicKey().ShouldBe(await world.Keys.PublicKeyAsync());
	}

	/// <summary>
	/// By value and by name: the token this handset holds, and any property
	/// that sounds like a credential. Link.Tests holds the names to an exact
	/// list and reads the bytes by hand.
	/// </summary>
	[Then(@"^the backup holds no token$")]
	public void NoCredential()
	{
		var opened = BackupFile.Open(Backup, Passphrase).ShouldNotBeNull();
		var json = JsonSerializer.Serialize(opened, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		opened.History.ShouldNotBeNull().Messages.Count.ShouldBeGreaterThan(0);

		json.ShouldNotContain(world.Sessions.Session.ShouldNotBeNull().Token, Case.Insensitive);
		json.ShouldNotContain("token", Case.Insensitive);
	}

	[Then(@"^the passphrase ""(.+)"" typed again as ""(.+)"" is (accepted|refused)$")]
	public void PassphraseAnswer(string first, string second, string answer)
	{
		var problem = BackupFile.PassphraseProblem(first, second);

		if (answer == "accepted")
		{
			problem.ShouldBeNull();
		}
		else
		{
			problem.ShouldNotBeNull();
		}
	}

	[Then(@"^the warning about a forgotten passphrase says ""(.+)""$")]
	public void Warns(string words) => MessageBackup.ForgottenPassphrase.ShouldContain(words);

	[Then(@"^the description of a backup says ""(.+)""$")]
	public void Describes(string words) => MessageBackup.OutsideLink.ShouldContain(words);
}
