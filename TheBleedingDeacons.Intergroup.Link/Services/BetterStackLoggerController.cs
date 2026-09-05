using Serilog;
using Serilog.Core;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// See <see cref="IBetterStackLoggerController"/>.
///
/// <para>Implementation notes:</para>
/// <list type="bullet">
/// <item>The <c>baseLoggerFactory</c> passed to the constructor
///       must build a <i>fresh</i> logger instance on every invocation — the
///       previous one is disposed on each reconfigure and a disposed logger
///       cannot be reused.</item>
/// <item>Calls are serialised by a lock so two settings-page saves in quick
///       succession can't race into half-built pipelines.</item>
/// <item>On failure, the previous logger is preserved in <c>Log.Logger</c>
///       and a warning is logged to it. That matches the original behaviour
///       in MauiProgram: a broken new config never takes down logging.</item>
/// </list>
/// </summary>
public sealed class BetterStackLoggerController : IBetterStackLoggerController
{
	private readonly Func<LoggerConfiguration> _baseLoggerFactory;
	private readonly HttpClient _httpClient;
	private readonly object _gate = new();

	// Tracks the currently-installed logger so we can dispose it on the next
	// reconfigure. We don't use Log.CloseAndFlush() because that would also
	// reach into the initial base logger from SetupSerilog that we want to
	// keep until we've installed a replacement.
	private Logger? _currentLogger;

	/// <summary>
	/// The configuration the live pipeline was built from, so {@see Flush}
	/// can rebuild an identical one rather than needing it passed in.
	/// </summary>
	private BetterStackConfiguration? _lastConfig;

	/// <summary>When the last forced flush ran, for debouncing.</summary>
	private DateTimeOffset _lastFlush = DateTimeOffset.MinValue;

	/// <summary>
	/// Shortest gap between forced flushes. A fault rarely arrives alone —
	/// one failure often logs an error from several layers on the way up —
	/// and rebuilding the pipeline once per event would cost more than the
	/// promptness is worth.
	/// </summary>
	private static readonly TimeSpan FlushDebounce = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The live controller, for callers that cannot be given one.
	///
	/// <para>Specifically <see cref="Support.FlushOnErrorSink"/>: it is
	/// constructed by the logger factory, which runs before the DI
	/// container exists, so it cannot be injected. A static handle is the
	/// smaller evil against making the logging pipeline depend on service
	/// resolution order.</para>
	/// </summary>
	public static IBetterStackLoggerController? Current { get; private set; }

	public BetterStackLoggerController(
		Func<LoggerConfiguration> baseLoggerFactory,
		HttpClient httpClient)
	{
		_baseLoggerFactory = baseLoggerFactory ?? throw new ArgumentNullException(nameof(baseLoggerFactory));
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

		Current = this;
	}

	public void Flush()
	{
		BetterStackConfiguration? config;

		lock (_gate)
		{
			config = _lastConfig;
			if (config is null)
			{
				// Nothing has been configured yet, so there is no durable sink
				// holding anything back.
				return;
			}

			var now = DateTimeOffset.UtcNow;
			if (now - _lastFlush < FlushDebounce)
			{
				return;
			}

			_lastFlush = now;
		}

		// Disposing the pipeline is what flushes the durable sink
		// (flushOnClose), and Reconfigure disposes the old one only after the
		// replacement is installed - so there is no window in which
		// Log.Logger points at nothing.
		Reconfigure(config);
	}

	public void Reconfigure(BetterStackConfiguration config)
	{
		lock (_gate)
		{
			Logger? newLogger = null;
			Logger? oldLogger;

			try
			{
				var builder = _baseLoggerFactory();

				if (config.IsValid())
				{
					var bufferDir = Path.Combine(FileSystem.AppDataDirectory, "logs", "betterstack-buffer");
					Directory.CreateDirectory(bufferDir);
					var bufferBaseFileName = Path.Combine(bufferDir, "buffer");

					var betterStackHttpClient = new BetterStackHttpClient(
						config.SourceToken,
						_httpClient);

					builder = builder.WriteTo.DurableHttpUsingFileSizeRolledBuffers(
						requestUri: config.Endpoint,
						bufferBaseFileName: bufferBaseFileName,
						bufferFileSizeLimitBytes: 8L * 1024 * 1024,
						retainedBufferFileCountLimit: 16,
						logEventsInBatchLimit: 500,
						batchSizeLimitBytes: 5L * 1024 * 1024,
						period: TimeSpan.FromSeconds(5),
						// Must be the Better Stack shape (dt/level/message), not Serilog's
						// stock JsonFormatter — see BetterStackTextFormatter. This is what
						// gets written into the buffer file, so it is also what determines
						// the timestamp Better Stack records for a batch that shipped late.
						textFormatter: new BetterStackTextFormatter(),
						batchFormatter: new BetterStackNdjsonBatchFormatter(),
						httpClient: betterStackHttpClient);
				}

				newLogger = builder.CreateLogger();
			}
			catch (Exception ex)
			{
				// Keep the existing logger running. The warning goes via Log,
				// which still points at the previous (working) pipeline.
				Log.Warning(ex,
					"Failed to build new Serilog pipeline for Better Stack config — retaining previous logger");
				newLogger?.Dispose();
				return;
			}

			// Swap atomically. Keep the previous logger reference so we can
			// dispose it after the swap — disposing the sink chain tears down
			// the durable HTTP shipper, which is essential when the token or
			// endpoint has changed.
			oldLogger = _currentLogger;
			_currentLogger = newLogger;
			_lastConfig = config;
			Log.Logger = newLogger;

			// Enable SelfLog so sink setup errors from the *new* logger are
			// visible in Debug output. Re-enabling each time is idempotent.
			Serilog.Debugging.SelfLog.Enable(msg =>
				System.Diagnostics.Debug.WriteLine($"[Serilog] {msg}"));

			if (config.IsValid())
			{
				Log.Information(
					"Better Stack sink (re)attached to {Endpoint}",
					config.ToLogSafe().Endpoint);
			}
			else
			{
				Log.Information("Better Stack sink removed (config invalid or cleared)");
			}

			// Disposing the old logger stops its background shipper loop and
			// releases the buffer bookmark file so the new logger can claim it.
			try
			{
				oldLogger?.Dispose();
			}
			catch (Exception ex)
			{
				// Disposal failures don't affect the new logger; just record them.
				Log.Debug(ex, "Error disposing previous Serilog pipeline");
			}
		}
	}
}