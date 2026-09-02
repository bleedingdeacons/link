using System.Reflection;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Builds Link's object graph, once, for both of the places that need it.
///
/// <para><b>Why this is not just MauiProgram.</b> The Android push
/// service runs with no UI and possibly with no app — Android starts it
/// to deliver a message even when Link has been swiped away — so the MAUI
/// host and its service provider may not exist at all. It needs the same
/// message service the app uses, wired the same way, and the only way to
/// guarantee "the same way" is for there to be one place that says how.
/// </para>
///
/// <para>Built lazily and held for the process lifetime. Constructing a
/// second graph would mean a second <see cref="JsonMessageHistory"/> over
/// the same file, and its write lock is per-instance.</para>
/// </summary>
public static class LinkServices
{
	private const string HistoryKeyName = "link_history_key";
	private const string HistoryFileName = "messages.bin";

	private static readonly Lock Gate = new();

	private static FellowshipConfiguration? _configuration;

	// Held for the process lifetime rather than scoped to Client's getter.
	// A new HttpClient per call is the classic way to exhaust sockets, and
	// this app makes a request every couple of minutes for as long as it is
	// open. The field is what keeps the one instance alive.
#pragma warning disable S1450 // See above: the lifetime is the point.
	private static HttpClient? _http;
#pragma warning restore S1450
	private static IFellowshipClient? _client;
	private static ISessionStore? _sessions;
	private static IDeviceKeyStore? _keys;
	private static IMessageHistory? _history;
	private static IMessageService? _messages;
	private static IPushRegistrar? _push;

	/// <summary>
	/// Where this build talks to, read once from the embedded
	/// appsettings.json.
	///
	/// <para>Embedded rather than copied to disk so it cannot be edited on
	/// a device to point the app at somebody else's server — which, given
	/// that the app hands that server an OAuth code, is worth the small
	/// inconvenience of a rebuild to change it.</para>
	/// </summary>
	public static FellowshipConfiguration Configuration
	{
		get
		{
			lock (Gate)
			{
				return _configuration ??= ReadConfiguration();
			}
		}
	}

	public static IPushRegistrar Push
	{
		get
		{
			lock (Gate)
			{
				return _push ??= new PushRegistrar();
			}
		}
	}

	public static ISessionStore Sessions
	{
		get
		{
			lock (Gate)
			{
				return _sessions ??= new SessionStore();
			}
		}
	}

	public static IDeviceKeyStore Keys
	{
		get
		{
			lock (Gate)
			{
				return _keys ??= new DeviceKeyStore();
			}
		}
	}

	public static IFellowshipClient Client
	{
		get
		{
			lock (Gate)
			{
				if (_client is not null)
				{
					return _client;
				}

				// One HttpClient for the process. A new one per call is the
				// classic way to exhaust sockets, and this app makes a
				// request every couple of minutes for as long as it is
				// open.
				_http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
				_client = new FellowshipClient(_http, Configuration);

				return _client;
			}
		}
	}

	public static IMessageHistory History
	{
		get
		{
			lock (Gate)
			{
				return _history ??= BuildHistory();
			}
		}
	}

	public static IMessageService Messages
	{
		get
		{
			lock (Gate)
			{
				return _messages ??= new MessageService(Client, History, Keys, Sessions);
			}
		}
	}

	private static FellowshipConfiguration ReadConfiguration()
	{
		try
		{
			using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("appsettings.json");
			if (stream is null)
			{
				return new FellowshipConfiguration();
			}

			using var document = JsonDocument.Parse(stream);

			if (!document.RootElement.TryGetProperty("Fellowship", out var section))
			{
				return new FellowshipConfiguration();
			}

			return section.Deserialize<FellowshipConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
				?? new FellowshipConfiguration();
		}
		catch (Exception e) when (e is JsonException or IOException)
		{
			// A build shipped without usable settings. Answering an
			// unconfigured object rather than throwing means the sign-in
			// screen can say "this app has not been set up" instead of the
			// app failing to start.
			Log.Error(e, "appsettings.json could not be read; this build has no intergroup to talk to");

			return new FellowshipConfiguration();
		}
	}

	/// <summary>
	/// The encrypted history, and the key it is encrypted with.
	///
	/// <para>The key is generated on first use and kept in
	/// <c>SecureStorage</c>. If it is ever unreadable a new one is made,
	/// and the existing history — which without the old key is
	/// unreadable bytes — reads as empty. That is the correct outcome: the
	/// alternative is an app that will not start because its cache will
	/// not decrypt.</para>
	/// </summary>
	private static JsonMessageHistory BuildHistory()
	{
		// Blocking on SecureStorage here rather than making every property
		// async. This runs once per process, on whichever thread got there
		// first, and an async chain would have to be threaded through the
		// push service's synchronous callback anyway.
		var stored = ReadHistoryKey();
		var key = JsonMessageHistory.KeyFrom(stored, out var toStore);

		if (!string.Equals(stored, toStore, StringComparison.Ordinal))
		{
			try
			{
				SecureStorage.SetAsync(HistoryKeyName, toStore).GetAwaiter().GetResult();
			}
#pragma warning disable CA1031 // Deliberately broad: see below.
			catch (Exception e)
#pragma warning restore CA1031
			{
				// A key that cannot be stored means a history that does not
				// survive a restart — the next launch generates a new key and
				// the old file reads as empty. That is a lost cache, not a
				// crash, and refusing to start over it would be worse.
				Log.Warning(e, "The history key could not be stored; this device's message history will not survive a restart");
			}
		}

		return new JsonMessageHistory(Path.Combine(FileSystem.AppDataDirectory, HistoryFileName), key);
	}

	private static string? ReadHistoryKey()
	{
		try
		{
			return SecureStorage.GetAsync(HistoryKeyName).GetAwaiter().GetResult();
		}
#pragma warning disable CA1031 // Deliberately broad: see BuildHistory.
		catch (Exception e)
#pragma warning restore CA1031
		{
			// Unreadable for any reason — a keystore whose key was
			// invalidated, a platform that threw rather than answering null.
			// All of them mean "no usable key", and BuildHistory makes a new
			// one.
			Log.Warning(e, "The history key could not be read; the stored history will read as empty");

			return null;
		}
	}
}
