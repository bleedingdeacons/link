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
		Assert.Equal(0, await history.HighestIdAsync());
	}

	[Fact]
	public async Task ClearingAnEmptyHistoryIsNotAnError()
	{
		using var history = New();

		await history.ClearAsync();

		Assert.Empty(await history.AllAsync());
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
