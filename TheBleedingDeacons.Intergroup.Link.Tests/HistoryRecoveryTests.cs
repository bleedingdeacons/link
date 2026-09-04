using System.Security.Cryptography;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// What the encrypted history does when the file on disk is not what it
/// expects.
///
/// <para><b>Reading as empty is a design decision, not a fallback.</b>
/// The alternative — refusing to start because a cache will not decrypt —
/// would take a member's whole app away over messages the server can send
/// again. Every path here therefore has to end in "no messages" rather
/// than in an exception, and the ones that matter are unreachable by hand:
/// nobody truncates their own history file to find out.</para>
/// </summary>
public sealed class HistoryRecoveryTests : IDisposable
{
	private readonly string _directory;
	private readonly string _path;
	private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

	public HistoryRecoveryTests()
	{
		_directory = Path.Combine(Path.GetTempPath(), "link-tests-" + Guid.NewGuid().ToString("N"));
		_path = Path.Combine(_directory, "messages.bin");
	}

	public void Dispose()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[Fact]
	public async Task AFileTooShortToBeAnEnvelopeReadsAsEmpty()
	{
		// 12 bytes of nonce plus 16 of tag is the floor. A file at or under
		// it cannot hold ciphertext, and slicing it would throw rather than
		// return — which on the message list means a crash instead of a
		// blank screen.
		Directory.CreateDirectory(_directory);
		await File.WriteAllBytesAsync(_path, new byte[20]);

		Assert.Empty(await New().AllAsync());
	}

	[Fact]
	public async Task AFileEncryptedWithAnotherKeyReadsAsEmpty()
	{
		// The real case: SecureStorage lost the key and a new one was
		// generated, so the existing file is unreadable bytes. That is a
		// lost cache, and the next sync refills it.
		await new JsonMessageHistory(_path, RandomNumberGenerator.GetBytes(32))
			.SaveAsync([Message(1, "Sealed to a key that is gone")]);

		Assert.Empty(await New().AllAsync());
	}

	[Fact]
	public async Task AFileOfRubbishReadsAsEmpty()
	{
		Directory.CreateDirectory(_directory);
		await File.WriteAllBytesAsync(_path, RandomNumberGenerator.GetBytes(256));

		Assert.Empty(await New().AllAsync());
	}

	[Fact]
	public async Task AHistoryThatWasNeverWrittenReadsAsEmpty()
	{
		// First launch. No file, no directory.
		Assert.Empty(await New().AllAsync());
		Assert.Equal(0, await New().HighestIdAsync());
	}

	[Fact]
	public async Task ClearingAHistoryThatIsNotThereIsNotAFailure()
	{
		// The member asked for it to be gone, and from where they are
		// standing it is.
		await New().ClearAsync();

		Assert.Empty(await New().AllAsync());
	}

	[Fact]
	public async Task MarkingAMessageReadThatIsNotHeldChangesNothing()
	{
		var history = New();
		await history.SaveAsync([Message(1, "Held")]);

		await history.MarkReadAsync(999);

		var all = await history.AllAsync();
		Assert.Single(all);
		Assert.False(all[0].IsRead);
	}

	[Fact]
	public async Task MarkingAnAlreadyReadMessageDoesNotMoveItsTimestamp()
	{
		// Re-marking happens whenever a message is opened twice. Rewriting
		// the timestamp would be a pointless disk write on the common path
		// and would make "when did I read this" wrong.
		var history = New();
		await history.SaveAsync([Message(1, "Held")]);

		await history.MarkReadAsync(1);
		var first = (await history.AllAsync())[0].ReadAt;

		await history.MarkReadAsync(1);
		var second = (await history.AllAsync())[0].ReadAt;

		Assert.NotNull(first);
		Assert.Equal(first, second);
	}

	[Fact]
	public async Task ClearingRemovesTheFileRatherThanEmptyingIt()
	{
		// A cleared history should leave nothing on disk to recover, which
		// is the whole point of the button.
		var history = New();
		await history.SaveAsync([Message(1, "Held")]);

		Assert.True(File.Exists(_path));

		await history.ClearAsync();

		Assert.False(File.Exists(_path));
	}

	private JsonMessageHistory New() => new(_path, _key);

	private static LinkMessage Message(long id, string subject) => new()
	{
		Id = id,
		Subject = subject,
		Body = "Body of " + subject,
		Sender = "Dave B",
		CreatedAt = 1788000000,
	};
}
