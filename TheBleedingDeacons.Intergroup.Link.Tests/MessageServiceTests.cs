using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The sync loop: what it fetches, what it stores, and when it tells the
/// server this handset is broken.
///
/// <para>Driven through the real cipher: an "opened" message here is one
/// that was actually sealed and actually decrypted, not one waved past
/// the crypto by a stubbed cipher. See <see cref="Sealing"/> on why the
/// envelope is built rather than loaded.</para>
/// </summary>
public sealed class MessageServiceTests
{
	[Fact]
	public async Task ItStoresWhatItCanOpen()
	{
		var sealed_ = Sealing.Seal();
		var client = new FakeClient { Inbox = Page(sealed_, unread: 1) };
		var history = new FakeHistory();

		var result = await Service(client, history, sealed_.PrivateKeyPem).SyncAsync();

		Assert.True(result.Succeeded);
		Assert.Equal(1, result.Received);
		Assert.Equal(1, result.Unread);
		Assert.False(result.KeyFault);

		Assert.Single(history.Held);
		Assert.Equal(Sealing.ExpectedId, history.Held[0].Id);
		Assert.Equal(Sealing.ExpectedSubject, history.Held[0].Subject);
	}

	[Fact]
	public async Task ItAsksForEverythingAboveWherePollingGotTo()
	{
		// This is what makes push optional. A message whose push was
		// dropped, delayed by Doze or sent to a rotated FCM token is
		// picked up here regardless.
		var client = new FakeClient();
		var history = new FakeHistory { PollFrom = 900 };

		await Service(client, history, "irrelevant").SyncAsync();

		Assert.Equal(900, client.AskedSince);
	}

	[Fact]
	public async Task ItWalksThroughEveryPageInOneSync()
	{
		using var mailbox = new Mailbox();
		var client = new FakeClient();
		client.Pages.Enqueue(new InboxPage { Messages = [mailbox.Envelope(1), mailbox.Envelope(2)], More = true });
		client.Pages.Enqueue(new InboxPage { Messages = [mailbox.Envelope(3)], Unread = 3 });
		var history = new FakeHistory();

		var result = await Service(client, history, mailbox.PrivateKey).SyncAsync();

		Assert.Equal([0, 2], client.AskedSinceEach);
		Assert.Equal(3, result.Received);
		Assert.Equal(3, result.Unread);
		Assert.Equal(3, history.PollFrom);
		Assert.Equal([1, 2, 3], history.Held.Select(m => m.Id).Order());
	}

	[Fact]
	public async Task APageThatOpensNothingLeavesThePollWhereItWas()
	{
		// A handset whose key has gone. Stepping past what it could not
		// open would lose those messages for good; staying put means a
		// replaced key brings every one of them back. And there is no
		// second page, because asking again would fetch this one again.
		using var mailbox = new Mailbox();
		var client = new FakeClient();
		client.Pages.Enqueue(new InboxPage { Messages = [mailbox.Envelope(1), mailbox.Envelope(2)], More = true });
		var history = new FakeHistory();

		var result = await Service(client, history, Sealing.StrangersPrivateKey()).SyncAsync();

		Assert.True(result.KeyFault);
		Assert.Single(client.AskedSinceEach);
		Assert.Equal(0, history.PollFrom);
		Assert.Equal(1, client.KeyFaultsReported);
	}

	[Fact]
	public async Task OneBadEnvelopeAmongGoodOnesIsSteppedOver()
	{
		// The key evidently works, so the bad one is the message's fault,
		// and asking for it again would only fetch the same bytes.
		using var mailbox = new Mailbox();
		var bad = mailbox.Envelope(1) with { Payload = Sealing.Seal().Payload };
		var client = new FakeClient { Inbox = new InboxPage { Messages = [bad, mailbox.Envelope(2)] } };
		var history = new FakeHistory();

		var result = await Service(client, history, mailbox.PrivateKey).SyncAsync();

		Assert.True(result.KeyFault);
		Assert.Equal(2, history.PollFrom);
	}

	[Fact]
	public async Task AMessageAlreadyHeldIsNotReceivedAgain()
	{
		// Every pushed message is collected once more by the poll. It must
		// not chime twice.
		using var mailbox = new Mailbox();
		var client = new FakeClient { Inbox = new InboxPage { Messages = [mailbox.Envelope(5)] } };
		var history = new FakeHistory();
		history.Held.Add(new LinkMessage { Id = 5, Subject = "Pushed" });

		var result = await Service(client, history, mailbox.PrivateKey).SyncAsync();

		Assert.Equal(0, result.Received);
		Assert.Equal(5, history.PollFrom);
		Assert.Single(history.Held);
	}

	[Fact]
	public async Task OneSyncWalksNoMoreThanItsShareOfPages()
	{
		using var mailbox = new Mailbox();
		var client = new FakeClient();
		for (var id = 1; id <= MessageService.PagesPerSync + 1; id++)
		{
			client.Pages.Enqueue(new InboxPage { Messages = [mailbox.Envelope(id)], More = true });
		}

		var history = new FakeHistory();

		await Service(client, history, mailbox.PrivateKey).SyncAsync();

		Assert.Equal(MessageService.PagesPerSync, client.AskedSinceEach.Count);
		Assert.Equal(MessageService.PagesPerSync, history.PollFrom);
	}

	[Fact]
	public async Task ALaterPageFailingKeepsWhatTheEarlierOnesBrought()
	{
		using var mailbox = new Mailbox();
		var client = new FakeClient();
		client.Pages.Enqueue(new InboxPage { Messages = [mailbox.Envelope(1)], More = true });
		client.Pages.Enqueue(InboxPage.Failed);
		var history = new FakeHistory();

		var result = await Service(client, history, mailbox.PrivateKey).SyncAsync();

		Assert.False(result.Succeeded);
		Assert.Single(history.Held);
		Assert.Equal(1, history.PollFrom);
	}

	[Fact]
	public async Task AnEmptyInboxIsNotAFailure()
	{
		// The ordinary answer to "anything new?". A caller that read this
		// as a failure would clear its unread badge on every quiet poll.
		var client = new FakeClient { Inbox = new InboxPage { Messages = [], Unread = 3 } };

		var result = await Service(client, new FakeHistory(), "key").SyncAsync();

		Assert.True(result.Succeeded);
		Assert.Equal(0, result.Received);
		Assert.Equal(3, result.Unread);
	}

	[Fact]
	public async Task ANetworkFailureIsDistinctFromAnEmptyInbox()
	{
		var client = new FakeClient { Inbox = InboxPage.Failed };

		var result = await Service(client, new FakeHistory(), "key").SyncAsync();

		Assert.False(result.Succeeded);
		// -1, not 0. Nothing should paint a badge from a number nobody got.
		Assert.Equal(-1, result.Unread);
	}

	[Fact]
	public async Task AMessageItCannotOpenIsReportedAsAKeyFault()
	{
		// The server cannot see this. From there a handset with a lost
		// private key looks perfectly healthy right up until a message it
		// cannot read, so the handset has to say so.
		var sealed_ = Sealing.Seal();
		var client = new FakeClient { Inbox = Page(sealed_) };

		// Signed in, holding a key — just not the one this was sealed to.
		var result = await Service(client, new FakeHistory(), Sealing.StrangersPrivateKey()).SyncAsync();

		Assert.True(result.KeyFault);
		Assert.Equal(0, result.Received);
		Assert.Equal(1, client.KeyFaultsReported);
	}

	[Fact]
	public async Task AHandsetWithNoKeyAtAllReportsTheSameFault()
	{
		// A factory reset or a restored backup. Same symptom, same report.
		var sealed_ = Sealing.Seal();
		var client = new FakeClient { Inbox = Page(sealed_) };

		var result = await Service(client, new FakeHistory(), privateKey: string.Empty).SyncAsync();

		Assert.True(result.KeyFault);
		Assert.Equal(1, client.KeyFaultsReported);
	}

	[Fact]
	public async Task TheFaultIsReportedOncePerSyncRatherThanOncePerMessage()
	{
		var sealed_ = Sealing.Seal();
		var client = new FakeClient
		{
			Inbox = new InboxPage
			{
				Messages = [sealed_.Envelope(1), sealed_.Envelope(2), sealed_.Envelope(3)],
			},
		};

		await Service(client, new FakeHistory(), privateKey: string.Empty).SyncAsync();

		Assert.Equal(1, client.KeyFaultsReported);
	}

	[Fact]
	public async Task SigningOutStopsTheSyncBeforeItAsksAnything()
	{
		var client = new FakeClient();
		var sessions = new FakeSessions { Session = null };

		var service = new MessageService(client, new FakeHistory(), new FakeKeys("key"), sessions);

		var result = await service.SyncAsync();

		Assert.False(result.Succeeded);
		Assert.False(client.InboxFetched);
	}

	[Fact]
	public async Task APushedEnvelopeTakesTheSamePathAsAPolledOne()
	{
		var sealed_ = Sealing.Seal();
		var history = new FakeHistory();

		var message = await Service(new FakeClient(), history, sealed_.PrivateKeyPem)
			.ReceivePushAsync(sealed_.WrappedKey, sealed_.Payload);

		Assert.NotNull(message);
		Assert.Equal(Sealing.ExpectedId, message.Id);
		Assert.Single(history.Held);
	}

	[Fact]
	public async Task APushedMessageAnnouncesItselfSoTheListCanRedraw()
	{
		// The list only reloads on OnAppearing or a pull, so without this
		// announcement a message arriving while somebody is looking at the
		// list leaves the screen stale — which is what happened on a real
		// handset: notification posted, message stored, list still saying
		// "No messages yet".
		var sealed_ = Sealing.Seal();
		var history = new FakeHistory();

		MessageReceived? heard = null;
		var token = new object();
		WeakReferenceMessenger.Default.Register<MessageReceived>(token, (_, m) => heard = m);

		try
		{
			await Service(new FakeClient(), history, sealed_.PrivateKeyPem)
				.ReceivePushAsync(sealed_.WrappedKey, sealed_.Payload);

			Assert.NotNull(heard);
			Assert.Equal(Sealing.ExpectedId, heard.Message.Id);

			// Announced only after the save, so a subscriber that reloads
			// from the history finds it there rather than racing it.
			Assert.Single(history.Held);
		}
		finally
		{
			WeakReferenceMessenger.Default.UnregisterAll(token);
		}
	}

	[Fact]
	public async Task APushThatCannotBeOpenedAnnouncesNothing()
	{
		var sealed_ = Sealing.Seal();

		var heard = 0;
		var token = new object();
		WeakReferenceMessenger.Default.Register<MessageReceived>(token, (_, _) => heard++);

		try
		{
			await Service(new FakeClient(), new FakeHistory(), privateKey: string.Empty)
				.ReceivePushAsync(sealed_.WrappedKey, sealed_.Payload);

			Assert.Equal(0, heard);
		}
		finally
		{
			WeakReferenceMessenger.Default.UnregisterAll(token);
		}
	}

	[Fact]
	public async Task APushItCannotOpenStoresNothingAndRaisesNothing()
	{
		// Answering a message here would put "New message" in the tray for
		// something the app cannot show. The next sync reports the fault
		// properly, with a session token to hand.
		var sealed_ = Sealing.Seal();
		var history = new FakeHistory();

		var message = await Service(new FakeClient(), history, privateKey: string.Empty)
			.ReceivePushAsync(sealed_.WrappedKey, sealed_.Payload);

		Assert.Null(message);
		Assert.Empty(history.Held);
	}

	[Fact]
	public async Task MarkingReadHappensLocallyEvenWhenTheServerCannotBeReached()
	{
		// The server is the authority on read state across a member's
		// devices. It is not the authority on whether this one redraws.
		var client = new FakeClient { MarkReadSucceeds = false };
		var history = new FakeHistory();

		await Service(client, history, "key").MarkReadAsync(12);

		Assert.Contains(12, history.MarkedRead);
	}

	[Fact]
	public async Task SendingWithoutASessionIsRefusedWithoutACall()
	{
		var client = new FakeClient();
		var service = new MessageService(client, new FakeHistory(), new FakeKeys("key"), new FakeSessions { Session = null });

		var result = await service.SendAsync(new SendRequest { Subject = "s", Body = "b" });

		Assert.False(result.Succeeded);
		Assert.False(client.SendCalled);
	}

	// ── Receipts ─────────────────────────────────────────────────────────

	[Fact]
	public async Task ASyncAcknowledgesWhatItOpened()
	{
		var sealed_ = Sealing.Seal();
		var client = new FakeClient { Inbox = Page(sealed_) };
		var history = new FakeHistory();

		await Service(client, history, sealed_.PrivateKeyPem).SyncAsync();

		Assert.Equal([Sealing.ExpectedId], Assert.Single(client.Acknowledgements));
		Assert.True(history.Held[0].Acknowledged);
	}

	[Fact]
	public async Task AnAcknowledgementTheServerRefusedIsTriedAgainNextTime()
	{
		// A Fellowship too old to know the route, or one having a bad day.
		// The message stays unacknowledged, so the next sync says it again.
		var client = new FakeClient { AcknowledgementAccepted = false };
		var history = new FakeHistory();
		history.Held.Add(new LinkMessage { Id = 12 });

		await Service(client, history, "key").SyncAsync();
		await Service(client, history, "key").SyncAsync();

		Assert.Equal(2, client.Acknowledgements.Count);
		Assert.False(history.Held[0].Acknowledged);
	}

	[Fact]
	public async Task WhatIsAlreadyAcknowledgedIsNotSaidTwice()
	{
		var client = new FakeClient();
		var history = new FakeHistory();
		history.Held.Add(new LinkMessage { Id = 12, Acknowledged = true });

		await Service(client, history, "key").SyncAsync();

		Assert.Empty(client.Acknowledgements);
	}

	[Fact]
	public async Task ASendKeepsACopyWithTheNamesItWentTo()
	{
		var client = new FakeClient();
		var history = new FakeHistory();

		await Service(client, history, "key").SendAsync(new SendRequest { Subject = "Moved", Body = "The 14th.", To = "Jo B" });

		var kept = Assert.Single(history.SentHeld);
		Assert.Equal("Moved", kept.Subject);
		Assert.Equal("Jo B", kept.To);
		Assert.Equal(1, kept.Recipients);
		Assert.Equal(ReceiptState.Sent, kept.State);
	}

	[Fact]
	public async Task ASyncAsksOnlyAboutSentMessagesStillWaiting()
	{
		var client = new FakeClient();
		var history = new FakeHistory();
		history.SentHeld.Add(new SentMessage { Id = 20, Recipients = 1, Received = 1, Read = 1 });
		history.SentHeld.Add(new SentMessage { Id = 21, Recipients = 1 });

		await Service(client, history, "key").SyncAsync();

		Assert.Equal([21L], Assert.Single(client.ReceiptsAskedFor));
	}

	[Fact]
	public async Task NewCountsAreKeptAndAnnounced()
	{
		var client = new FakeClient { Receipts = [new MessageReceipt(21, 2, 2, 1)] };
		var history = new FakeHistory();
		history.SentHeld.Add(new SentMessage { Id = 21, Recipients = 2 });
		var announced = new List<ReceiptsChanged>();
		var listener = new object();
		WeakReferenceMessenger.Default.Register<ReceiptsChanged>(listener, (_, changed) => announced.Add(changed));

		try
		{
			await Service(client, history, "key").SyncAsync();
		}
		finally
		{
			WeakReferenceMessenger.Default.Unregister<ReceiptsChanged>(listener);
		}

		Assert.Equal(ReceiptState.Received, history.SentHeld[0].State);
		Assert.Equal([21L], Assert.Single(announced).MessageIds);
	}

	[Fact]
	public async Task ReceiptsThatCannotBeFetchedDoNotFailTheSync()
	{
		var client = new FakeClient { Receipts = null };
		var history = new FakeHistory();
		history.SentHeld.Add(new SentMessage { Id = 21, Recipients = 1 });

		var result = await Service(client, history, "key").SyncAsync();

		Assert.True(result.Succeeded);
		Assert.Equal(ReceiptState.Sent, history.SentHeld[0].State);
	}

	// ── Constructor guards ───────────────────────────────────────────────
	//
	// Four dependencies, all resolved from the container. A missing
	// registration otherwise surfaces as a NullReferenceException on the
	// first sync, a long way from the MauiProgram line that caused it —
	// and on a handset, where the stack trace goes to a log file nobody
	// reads. Each guard names the parameter it is about, so the message
	// says which registration is missing.

	[Theory]
	[InlineData("client")]
	[InlineData("history")]
	[InlineData("keys")]
	[InlineData("sessions")]
	public void EveryDependencyIsRequiredByName(string missing)
	{
		var exception = Assert.Throws<ArgumentNullException>(() => new MessageService(
			missing == "client" ? null! : new FakeClient(),
			missing == "history" ? null! : new FakeHistory(),
			missing == "keys" ? null! : new FakeKeys("key"),
			missing == "sessions" ? null! : new FakeSessions()));

		Assert.Equal(missing, exception.ParamName);
	}

	private static MessageService Service(FakeClient client, FakeHistory history, string privateKey) =>
		new(client, history, new FakeKeys(privateKey), new FakeSessions());

	/// <summary>One sealed envelope, as a page of the inbox.</summary>
	private static InboxPage Page(Sealing.Sealed sealed_, int unread = 0) => new()
	{
		Messages = [sealed_.Envelope()],
		Unread = unread,
	};

	/// <summary>
	/// One keypair, and as many envelopes sealed to it as a test wants,
	/// each carrying its own id — for the tests that need a page of
	/// several messages one key opens.
	/// </summary>
	private sealed class Mailbox : IDisposable
	{
		private readonly RSA _rsa = RSA.Create(Sealing.KeyBits);

		public string PrivateKey => _rsa.ExportPkcs8PrivateKeyPem();

		public SealedMessage Envelope(long id)
		{
			var payload = Sealing.Payload();
			payload["id"] = id;

			return Sealing.SealTo(_rsa, payload).Envelope(id);
		}

		public void Dispose() => _rsa.Dispose();
	}

	// ── Doubles ──────────────────────────────────────────────────────────
	//
	// Hand-written rather than mocked. There are four small interfaces
	// here and each fake records the one or two things a test asserts on;
	// a mocking framework would add a dependency and a second syntax to
	// read for no more expressive power.

	private sealed class FakeClient : IFellowshipClient
	{
		public InboxPage Inbox { get; set; } = new() { Messages = [] };

		public bool MarkReadSucceeds { get; set; } = true;

		public long AskedSince { get; private set; } = -1;

		public bool InboxFetched { get; private set; }

		public int KeyFaultsReported { get; private set; }

		public bool SendCalled { get; private set; }

		/// <summary>
		/// Pages to answer with, in order, before falling back to
		/// <see cref="Inbox"/>. For the tests about walking through more
		/// than one.
		/// </summary>
		public Queue<InboxPage> Pages { get; } = [];

		/// <summary>Every <c>sinceId</c> asked with, in order.</summary>
		public List<long> AskedSinceEach { get; } = [];

		public Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default)
		{
			InboxFetched = true;
			AskedSince = sinceId;
			AskedSinceEach.Add(sinceId);

			return Task.FromResult(Pages.Count > 0 ? Pages.Dequeue() : Inbox);
		}

		public Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default)
		{
			KeyFaultsReported++;

			return Task.FromResult(true);
		}

		public Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default) =>
			Task.FromResult(MarkReadSucceeds);

		/// <summary>Every batch of ids acknowledged, in order.</summary>
		public List<IReadOnlyCollection<long>> Acknowledgements { get; } = [];

		public bool AcknowledgementAccepted { get; set; } = true;

		/// <summary>What a receipts request answers, or null for one that never arrived.</summary>
		public IReadOnlyList<MessageReceipt>? Receipts { get; set; } = [];

		public List<IReadOnlyCollection<long>> ReceiptsAskedFor { get; } = [];

		public Task<bool> MarkReceivedAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default)
		{
			Acknowledgements.Add(messageIds);

			return Task.FromResult(AcknowledgementAccepted);
		}

		public Task<IReadOnlyList<MessageReceipt>?> FetchReceiptsAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default)
		{
			ReceiptsAskedFor.Add(messageIds);

			return Task.FromResult(Receipts);
		}

		public Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default)
		{
			SendCalled = true;

			return Task.FromResult(new SendResult { MessageId = 1, Recipients = 1 });
		}

		public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default) =>
			Task.FromResult<SignInStart?>(null);

		public Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(EnrolmentResult.Failed("not used"));

		// Sign-in surface, present to satisfy the interface. These tests are
		// about what happens to messages once a handset is already enrolled.
		public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) =>
			Task.FromResult(PasswordSetResult.Failed("not used"));

		public Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default) =>
			Task.FromResult(FellowshipDirectory.Empty);

		public Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);

		public Task<RotateKeyResult> RotateKeyAsync(string token, RotateKeyRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(RotateKeyResult.Ok());

		public Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);
	}

	private sealed class FakeHistory : IMessageHistory
	{
		public List<LinkMessage> Held { get; } = [];

		public List<long> MarkedRead { get; } = [];

		/// <summary>Where the next poll starts. Only a poll moves it, as in the real one.</summary>
		public long PollFrom { get; set; }

		public Task<IReadOnlyList<LinkMessage>> AllAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<LinkMessage>>(Held);

		public Task SaveAsync(IEnumerable<LinkMessage> messages, CancellationToken cancellationToken = default)
		{
			foreach (var message in messages.ToList())
			{
				Held.RemoveAll(m => m.Id == message.Id);
				Held.Add(message);
			}

			return Task.CompletedTask;
		}

		public Task<long> PollFromAsync(CancellationToken cancellationToken = default) => Task.FromResult(PollFrom);

		public Task MarkPolledAsync(long messageId, CancellationToken cancellationToken = default)
		{
			PollFrom = Math.Max(PollFrom, messageId);

			return Task.CompletedTask;
		}

		public List<SentMessage> SentHeld { get; } = [];

		public Task MarkAcknowledgedAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default)
		{
			foreach (var id in messageIds.ToList())
			{
				var index = Held.FindIndex(m => m.Id == id);
				if (index >= 0)
				{
					Held[index] = Held[index] with { Acknowledged = true };
				}
			}

			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<SentMessage>> SentAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<SentMessage>>(SentHeld.OrderByDescending(m => m.Id).ToList());

		public Task SaveSentAsync(SentMessage message, CancellationToken cancellationToken = default)
		{
			SentHeld.RemoveAll(m => m.Id == message.Id);
			SentHeld.Add(message);

			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<long>> ApplyReceiptsAsync(IEnumerable<MessageReceipt> receipts, CancellationToken cancellationToken = default)
		{
			var changed = new List<long>();

			foreach (var receipt in receipts)
			{
				var index = SentHeld.FindIndex(m => m.Id == receipt.Id);
				if (index >= 0 && SentHeld[index].With(receipt) != SentHeld[index])
				{
					SentHeld[index] = SentHeld[index].With(receipt);
					changed.Add(receipt.Id);
				}
			}

			return Task.FromResult<IReadOnlyList<long>>(changed);
		}

		public Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default)
		{
			MarkedRead.Add(messageId);

			return Task.CompletedTask;
		}

		public Task DeleteAsync(long messageId, CancellationToken cancellationToken = default)
		{
			Held.RemoveAll(m => m.Id == messageId);
			SentHeld.RemoveAll(m => m.Id == messageId);

			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<DeletedMessage>> DeletedAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<DeletedMessage>>([]);

		public Task ClearAsync(CancellationToken cancellationToken = default)
		{
			PollFrom = Held.Count == 0 ? PollFrom : Math.Max(PollFrom, Held.Max(m => m.Id));
			Held.Clear();

			return Task.CompletedTask;
		}

		public long Owner { get; private set; }

		public Task AdoptAsync(long memberId, CancellationToken cancellationToken = default)
		{
			if (Owner != memberId || memberId <= 0)
			{
				Held.Clear();
				PollFrom = 0;
			}

			Owner = memberId;

			return Task.CompletedTask;
		}

		public Task ResetAsync(CancellationToken cancellationToken = default)
		{
			Held.Clear();
			PollFrom = 0;

			return Task.CompletedTask;
		}
	}

	private sealed class FakeKeys(string privateKey) : IDeviceKeyStore
	{
		public Task<bool> HasKeyAsync() => Task.FromResult(!string.IsNullOrEmpty(privateKey));

		public Task<string> RegenerateAsync() => Task.FromResult("public");

		public Task<string> PublicKeyAsync() => Task.FromResult("public");

		public Task<string> PrivateKeyAsync() => Task.FromResult(privateKey);

		public Task ClearAsync() => Task.CompletedTask;
	}

	private sealed class FakeSessions : ISessionStore
	{
		public DeviceSession? Session { get; set; } = new() { Token = "fdt_test", DeviceId = 1, MemberId = 2 };

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

}
