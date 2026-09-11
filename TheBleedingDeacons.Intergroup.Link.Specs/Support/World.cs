using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Specs.Support;

/// <summary>
/// One handset, and everything around it, for the length of a scenario.
///
/// <para>Reqnroll gives each scenario its own instance through its
/// container, so a step class takes this in its constructor and every
/// step in the scenario sees the same handset. The container disposes it
/// afterwards, which is what removes the temporary directory below.</para>
///
/// <para><b>The history is the real one, over a real file.</b> Hand's
/// specs made the same choice for the same reason: the scenarios that use
/// it are about what a handset remembers, and a stubbed history would let
/// them pass while the shipping one wrote nothing. So
/// <see cref="JsonMessageHistory"/> stays in the path — AES envelope,
/// JSON round trip, cleared-up-to mark and all — and what is stood in for
/// is only the platform underneath it: a temporary directory instead of
/// <c>FileSystem.AppDataDirectory</c>, and 32 bytes instead of
/// <c>SecureStorage</c>.</para>
///
/// <para><b>The service is built on first use, not in the
/// constructor.</b> A Given step can sign the handset out or take its
/// key away, and those have to have had their say before anything reads
/// them.</para>
/// </summary>
public sealed class World : IDisposable
{
	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), "link-specs", Guid.NewGuid().ToString("N"));

	private readonly byte[] _historyKey = RandomNumberGenerator.GetBytes(32);

	private JsonMessageHistory? _history;
	private MessageService? _handset;

	public World() =>
		// Registered for the whole scenario rather than by the step that
		// asks, because the announcement is sent during the arrival and
		// there is nowhere later to have been listening from.
		WeakReferenceMessenger.Default.Register<World, MessageReceived>(
			this, static (world, announcement) => world.Announced.Add(announcement.Message));

	public FakeFellowshipClient Fellowship { get; } = new();

	public FakeDeviceKeyStore Keys { get; } = new();

	public FakeSessionStore Sessions { get; } = new();

	/// <summary>Every message announced to whoever is on screen, in order.</summary>
	public List<LinkMessage> Announced { get; } = [];

	/// <summary>Every envelope this scenario has sealed, by message id.</summary>
	public Dictionary<long, SealedMessage> Built { get; } = [];

	/// <summary>What the last sync did.</summary>
	public SyncResult? LastSync { get; set; }

	/// <summary>What the last send answered.</summary>
	public SendResult? LastSend { get; set; }

	/// <summary>What the last pushed envelope opened as, or null.</summary>
	public LinkMessage? Opened { get; set; }

	/// <summary>The push status a scenario assembled.</summary>
	public PushStatus Status { get; set; } = PushStatus.Unknown;

	/// <summary>The configuration a scenario is examining, before it is used.</summary>
	public FellowshipConfiguration? Settings { get; set; }

	/// <summary>The address book a compose scenario is working from.</summary>
	public List<Recipient> Addressable { get; } = [];

	/// <summary>Who the message being composed is going to.</summary>
	public List<Recipient> Chosen { get; } = [];

	/// <summary>What the last search of the address book turned up.</summary>
	public List<Recipient> Found { get; } = [];

	/// <summary>The member a scenario is asking about a row for.</summary>
	public DirectoryMember? Member { get; set; }

	public JsonMessageHistory History => _history ??= new JsonMessageHistory(Path.Combine(_directory, "messages.bin"), _historyKey);

	public MessageService Handset =>
		_handset ??= new MessageService(Fellowship, History, Keys, Sessions);

	/// <summary>
	/// Kill the app and open it again, keeping only what was written down.
	///
	/// <para>A phone is killed rather than closed — swiped away, or taken
	/// for memory — so "does this survive a restart" is a question the
	/// history has to answer, and the only honest way to ask it is to
	/// build a second one over the same file.</para>
	/// </summary>
	public void Restart()
	{
		_history?.Dispose();
		_history = null;
		_handset = null;
	}

	/// <summary>
	/// An envelope sealed to this handset, as Fellowship sends it.
	/// </summary>
	public SealedMessage Envelope(long id, string subject = "", string sender = "", long replyTo = 0, long readAt = 0)
	{
		var payload = Sealing.Payload(id);

		if (subject.Length > 0)
		{
			payload["subject"] = subject;
		}

		if (sender.Length > 0)
		{
			payload["sender"] = sender;
		}

		payload["reply_to"] = replyTo;
		payload["read_at"] = readAt;

		var envelope = Sealing.Seal(id, payload, Keys.SealingKey);

		Built[id] = envelope;

		return envelope;
	}

	/// <summary>An envelope sealed to a handset that is not this one.</summary>
	public SealedMessage EnvelopeForSomebodyElse(long id) =>
		Sealing.Seal(id, Sealing.Payload(id), Sealing.NewKeypair().PublicKey);

	/// <summary>
	/// Put envelopes in front of the next sync, keeping whatever is
	/// already there.
	///
	/// <para>Additive rather than replacing, because a feature file builds
	/// a page a line at a time — "message 12 is waiting" and then "message
	/// 13 was sealed to another handset" describe one page with two
	/// envelopes on it, which is the whole point of that scenario.</para>
	/// </summary>
	public void Waiting(params SealedMessage[] envelopes) =>
		Fellowship.Inbox = Fellowship.Inbox with
		{
			Messages = [.. Fellowship.Inbox.Messages, .. envelopes],
		};

	/// <summary>What the handset holds, newest first.</summary>
	public async Task<IReadOnlyList<LinkMessage>> HeldAsync() => await History.AllAsync();

	/// <summary>The message this handset holds with this id, or null.</summary>
	public async Task<LinkMessage?> HeldAsync(long id) =>
		(await History.AllAsync()).FirstOrDefault(m => m.Id == id);

	public void Dispose()
	{
		WeakReferenceMessenger.Default.Unregister<MessageReceived>(this);

		_history?.Dispose();

		try
		{
			if (Directory.Exists(_directory))
			{
				Directory.Delete(_directory, recursive: true);
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			// A temporary directory that will not go is the operating
			// system's problem, not a reason to fail a scenario that has
			// already passed.
		}
	}
}
