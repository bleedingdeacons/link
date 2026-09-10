using System.Security.Cryptography;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The on-device history: what it keeps, what it replaces, and what
/// clearing actually does.
/// </summary>
public sealed class JsonMessageHistoryTests : IDisposable
{
	private readonly string _directory;
	private readonly string _path;
	private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

	public JsonMessageHistoryTests()
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
	public async Task ItKeepsWhatItIsGiven()
	{
		using var history = New();

		await history.SaveAsync([Message(1, "First"), Message(2, "Second")]);

		var held = await history.AllAsync();

		Assert.Equal(2, held.Count);
		// Newest first: a message list is read from the top.
		Assert.Equal(2, held[0].Id);
	}

	[Fact]
	public async Task ItSurvivesBeingReopened()
	{
		using (var writing = New())
		{
			await writing.SaveAsync([Message(7, "Kept")]);
		}

		using var reading = New();
		var held = await reading.AllAsync();

		Assert.Single(held);
		Assert.Equal("Kept", held[0].Subject);
	}

	[Fact]
	public async Task WhatIsOnDiskIsNotReadable()
	{
		// The point of encrypting at rest. A phone handed to somebody at a
		// repair shop should not yield a folder of legible fellowship
		// business.
		using var history = New();

		await history.SaveAsync([Message(1, "Confidential subject line")]);

		var raw = await File.ReadAllBytesAsync(_path);
		var asText = System.Text.Encoding.UTF8.GetString(raw);

		Assert.DoesNotContain("Confidential", asText, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ADifferentKeyReadsAsNoHistoryRatherThanFailing()
	{
		// A messaging app that will not start because its cache will not
		// decrypt is a worse outcome than one that has lost its cache.
		using (var writing = New())
		{
			await writing.SaveAsync([Message(1, "Written under the old key")]);
		}

		using var stranger = new JsonMessageHistory(_path, RandomNumberGenerator.GetBytes(32));

		Assert.Empty(await stranger.AllAsync());
	}

	[Fact]
	public async Task ALaterCopyOfAMessageReplacesTheEarlierOne()
	{
		// The same message arrives by push and again by poll, and only the
		// poll's copy carries the read flag.
		using var history = New();

		await history.SaveAsync([Message(3, "Subject")]);
		await history.SaveAsync([Message(3, "Subject") with { ReadAt = 1788000000 }]);

		var held = await history.AllAsync();

		Assert.Single(held);
		Assert.True(held[0].IsRead);
	}

	[Fact]
	public async Task ALocalReadIsNotUndoneByAnUnreadCopyFromTheServer()
	{
		// The one thing a replace must not lose: a read that has not
		// reached the server yet. Otherwise a message read on the train
		// goes bold again the moment the phone finds signal.
		using var history = New();

		await history.SaveAsync([Message(4, "Subject")]);
		await history.MarkReadAsync(4);

		await history.SaveAsync([Message(4, "Subject")]);

		var held = await history.AllAsync();

		Assert.True(held[0].IsRead);
	}

	[Fact]
	public async Task TheHighestIdIsWhatAPollAsksFor()
	{
		using var history = New();

		Assert.Equal(0, await history.HighestIdAsync());

		await history.SaveAsync([Message(4, "a"), Message(11, "b"), Message(7, "c")]);

		Assert.Equal(11, await history.HighestIdAsync());
	}

	[Fact]
	public async Task ClearingRemovesEverything()
	{
		using var history = New();

		await history.SaveAsync([Message(1, "Gone"), Message(2, "Also gone")]);
		await history.ClearAsync();

		Assert.Empty(await history.AllAsync());
	}

	[Fact]
	public async Task ClearedMessagesStayClearedRatherThanBeingFetchedBackAgain()
	{
		// The whole point of the mark. A poll asks for everything above
		// the highest id held, so a store that dropped back to 0 had the
		// server refill it seconds later, in front of somebody who had
		// just been told their messages were cleared.
		using var history = New();

		await history.SaveAsync([Message(1, "Gone"), Message(2, "Also gone")]);
		await history.ClearAsync();

		Assert.Equal(2, await history.HighestIdAsync());
	}

	[Fact]
	public async Task TheMarkSurvivesBeingReopened()
	{
		using (var writing = New())
		{
			await writing.SaveAsync([Message(9, "Gone")]);
			await writing.ClearAsync();
		}

		using var reopened = New();

		Assert.Empty(await reopened.AllAsync());
		Assert.Equal(9, await reopened.HighestIdAsync());
	}

	[Fact]
	public async Task AMessageArrivingAfterAClearMovesThePollPastTheMark()
	{
		using var history = New();

		await history.SaveAsync([Message(4, "Gone")]);
		await history.ClearAsync();
		await history.SaveAsync([Message(5, "New")]);

		Assert.Equal(5, await history.HighestIdAsync());
		Assert.Single(await history.AllAsync());
	}

	[Fact]
	public async Task ClearingAnEmptyHistoryIsNotAnError()
	{
		using var history = New();

		await history.ClearAsync();

		Assert.Empty(await history.AllAsync());
	}

	[Fact]
	public async Task ClearingTwiceDoesNotUndoTheFirstClear()
	{
		// The second clear finds nothing held, so a mark taken from the
		// messages alone would be 0 — and everything the first clear
		// removed would arrive again on the next poll.
		using var history = New();

		await history.SaveAsync([Message(6, "Gone")]);
		await history.ClearAsync();
		await history.ClearAsync();

		Assert.Equal(6, await history.HighestIdAsync());
	}

	[Fact]
	public async Task ResettingForgetsTheMarkAsWell()
	{
		// Signing out. The next member on this handset must fetch their
		// own history from the beginning rather than inherit somebody
		// else's idea of where to start.
		using var history = New();

		await history.SaveAsync([Message(3, "Someone else's")]);
		await history.ClearAsync();
		await history.ResetAsync();

		Assert.Empty(await history.AllAsync());
		Assert.Equal(0, await history.HighestIdAsync());
	}

	[Fact]
	public async Task AHistoryWrittenBeforeTheMarkExistedIsStillRead()
	{
		// Handsets are carrying files in the old shape — a bare array
		// rather than an object — and reading one as corrupt would throw
		// away the history of every phone that upgrades.
		await WriteLegacyFileAsync();

		using var history = New();

		Assert.Equal(8, Assert.Single(await history.AllAsync()).Id);
		Assert.Equal(8, await history.HighestIdAsync());
	}

	[Fact]
	public async Task AHistoryInTheOldShapeCanStillBeCleared()
	{
		await WriteLegacyFileAsync();

		using var history = New();

		await history.ClearAsync();

		Assert.Empty(await history.AllAsync());
		Assert.Equal(8, await history.HighestIdAsync());
	}

	[Fact]
	public async Task MessagesWithoutAnIdAreNotKept()
	{
		// An id is what marks read, replies and de-duplicates. A message
		// without one cannot be any of those things.
		using var history = New();

		await history.SaveAsync([Message(0, "No id")]);

		Assert.Empty(await history.AllAsync());
	}

	[Fact]
	public void AWrongLengthKeyIsRefusedAtConstruction()
	{
		// A wiring mistake in the app, not a runtime condition — and a
		// history written under a truncated key is the kind of thing
		// nobody notices until it matters.
		Assert.Throws<ArgumentException>(() => new JsonMessageHistory(_path, new byte[16]));
	}

	[Fact]
	public void AStoredKeyIsReusedAndAMissingOneIsGenerated()
	{
		var generated = JsonMessageHistory.KeyFrom(null, out var toStore);

		Assert.Equal(32, generated.Length);
		Assert.NotEmpty(toStore);

		var reused = JsonMessageHistory.KeyFrom(toStore, out var unchanged);

		Assert.Equal(generated, reused);
		Assert.Equal(toStore, unchanged);
	}

	[Fact]
	public void AKeyOfTheWrongLengthIsReplacedRatherThanUsed()
	{
		var replaced = JsonMessageHistory.KeyFrom(Convert.ToBase64String(new byte[8]), out var toStore);

		Assert.Equal(32, replaced.Length);
		Assert.NotEqual(Convert.ToBase64String(new byte[8]), toStore);
	}

	/// <summary>
	/// A history file as builds before the clear mark wrote it: the bare
	/// array, with no object around it.
	/// </summary>
	private const string LegacyFile =
		"""[{"id":8,"subject":"Older build","body":"b","sender":"Dave B","createdAt":1788000000}]""";

	private JsonMessageHistory New() => new(_path, _key);

	/// <summary>
	/// Pack plaintext into the envelope the store reads: a 12-byte nonce,
	/// the 16-byte tag, then the ciphertext.
	/// </summary>
	/// <remarks>
	/// Written out here rather than reached for through the class under
	/// test, because these tests exist to prove a file this class did not
	/// write is still readable — and a helper that used its own writer
	/// could only ever produce the shape it writes today.
	/// </remarks>
	private async Task WriteLegacyFileAsync()
	{
		// The store creates its own directory when it writes; nothing has
		// written yet when a test plants a file by hand.
		Directory.CreateDirectory(_directory);

		await File.WriteAllBytesAsync(_path, Sealed(LegacyFile));
	}

	private byte[] Sealed(string json)
	{
		var plaintext = System.Text.Encoding.UTF8.GetBytes(json);

		var nonce = RandomNumberGenerator.GetBytes(12);
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var gcm = new AesGcm(_key, 16))
		{
			gcm.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
		nonce.CopyTo(packed, 0);
		tag.CopyTo(packed, nonce.Length);
		ciphertext.CopyTo(packed, nonce.Length + tag.Length);

		return packed;
	}

	private static LinkMessage Message(long id, string subject) => new()
	{
		Id = id,
		Subject = subject,
		Body = "Body of " + subject,
		Sender = "Dave B",
		CreatedAt = 1788000000,
	};
}
