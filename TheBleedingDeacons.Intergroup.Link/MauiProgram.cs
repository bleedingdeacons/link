using System.Reflection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support;
using TheBleedingDeacons.Intergroup.Link.ViewModels;
using TheBleedingDeacons.Intergroup.Link.Views;

namespace TheBleedingDeacons.Intergroup.Link;

public static class MauiProgram
{
	// Produces a fresh base-logger configuration (file / debug / console sinks
	// plus every enricher). Captured during SetupSerilog so
	// BetterStackLoggerController can rebuild the whole pipeline on demand.
	// Null until SetupSerilog has run.
	private static Func<LoggerConfiguration>? _baseLoggerFactory;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		// ── Load appsettings.json from the embedded resource ──────────
		// MAUI does not pick appsettings.json up the way ASP.NET Core does.
		// The file is embedded (see the csproj) and has to be loaded by hand
		// so Serilog's ReadFrom.Configuration and the BetterStack lookup
		// below actually return something.
		//
		// LinkServices reads the same resource with JsonDocument for the
		// Fellowship section, and goes on doing so: it runs in the headless
		// push process where there is no MauiAppBuilder and no
		// IConfiguration to read from.
		var assembly = Assembly.GetExecutingAssembly();
		using (var stream = assembly.GetManifestResourceStream("appsettings.json"))
		{
			if (stream is not null)
			{
				var json = new ConfigurationBuilder()
					.AddJsonStream(stream)
					.Build();
				builder.Configuration.AddConfiguration(json);
			}
			else
			{
				System.Diagnostics.Debug.WriteLine(
					"WARNING: appsettings.json embedded resource not found. Available resources: "
					+ string.Join(", ", assembly.GetManifestResourceNames()));
			}
		}

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		SetupSerilog(builder);

		// Bridge Serilog into Microsoft.Extensions.Logging, so an ILogger<T>
		// resolved from DI flows through the same pipeline.
		builder.Logging.AddSerilog();

		// Flush Serilog on unhandled and fatal errors.
		RegisterGlobalExceptionHandlers();

		// The singletons come from LinkServices rather than being
		// constructed here, and that is the whole point of it: the Android
		// push service runs with no MAUI host and needs the *same* graph.
		// Registering fresh instances in this container would give the app
		// a second JsonMessageHistory over the same file, whose write lock
		// is per-instance.
		builder.Services.AddSingleton(LinkServices.Configuration);
		builder.Services.AddSingleton(LinkServices.Client);
		builder.Services.AddSingleton(LinkServices.Sessions);
		builder.Services.AddSingleton(LinkServices.Keys);
		builder.Services.AddSingleton(LinkServices.History);
		builder.Services.AddSingleton(LinkServices.Messages);
		builder.Services.AddSingleton(LinkServices.Push);
		builder.Services.AddSingleton<IUiDispatcher, MainThreadDispatcher>();
		builder.Services.AddSingleton<IAppleSignIn, AppleSignIn>();
		builder.Services.AddSingleton<DeviceAuthService>();

		// The HttpClient the log shipper uses, and nothing else.
		//
		// Managed SocketsHttpHandler on every platform, with an aggressive
		// pooled-connection idle timeout. Hand documents the race this
		// avoids: WinHttpHandler surfaces a server-closed keep-alive
		// connection as WinHttpException 12152 on the next reuse, which
		// fires on CloseAndFlush at shutdown after the sink has been idle.
		//
		// Keyed, and separate from the client LinkServices holds for talking
		// to Fellowship: that one is deliberately a platform-native handler,
		// because Fellowship sits behind an edge WAF that fingerprints TLS.
		// Better Stack does not.
		builder.Services.AddKeyedSingleton<HttpClient>("betterstack", (_, _) => CreateBetterStackHttpClient());

		// Rebuilds the Serilog pipeline on demand. Singleton so every caller
		// shares the serialisation lock inside it.
		builder.Services.AddSingleton<IBetterStackLoggerController>(sp =>
		{
			if (_baseLoggerFactory is null)
			{
				throw new InvalidOperationException(
					"Serilog base-logger factory was not captured. SetupSerilog must run before the DI container is built.");
			}

			var httpClient = sp.GetRequiredKeyedService<HttpClient>("betterstack");
			return new BetterStackLoggerController(_baseLoggerFactory, httpClient);
		});

		builder.Services.AddSingleton<SignInViewModel>();
		builder.Services.AddSingleton<MessagesViewModel>();
		builder.Services.AddSingleton<ComposeViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();

		builder.Services.AddSingleton<SignInPage>();
		builder.Services.AddSingleton<MessagesPage>();
		builder.Services.AddTransient<ComposePage>();
		builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// ── Attach the Better Stack sink ──────────────────────────────
		// SetupSerilog runs before the container exists, so it cannot build
		// the durable HTTP sink itself. Once it does, the controller layers
		// that sink onto the base pipeline. A configuration with no token —
		// which is what a build ships with unless somebody put one in — is
		// a supported state: the controller takes its "not configured"
		// branch and the app logs locally only.
		using (var scope = app.Services.CreateScope())
		{
			var controller = scope.ServiceProvider.GetRequiredService<IBetterStackLoggerController>();
			controller.Reconfigure(ReadBetterStackConfiguration(builder.Configuration));
		}

		return app;
	}

	/// <summary>
	/// Serilog, as Hand sets it up: a rolling file, the IDE in Debug, the
	/// console on desktop, every enricher, and a durable Better Stack sink
	/// layered on afterwards.
	///
	/// <para><b>Why Link now ships to Better Stack too.</b> This file used
	/// to argue against it, on the grounds that Link's failures are late
	/// messages rather than a helpline alert that did not ring, and that
	/// the file could be pulled with adb when somebody was diagnosing. The
	/// second half is what did not hold up: pulling a file needs the
	/// handset, a cable and somebody who knows to ask, and the failures
	/// that matter here — a message that arrived and would not open, a push
	/// that silently stopped — are exactly the ones a member does not
	/// report because they cannot see them happening. The token is still a
	/// credential shipped in an app, which is why it is empty unless
	/// somebody deliberately puts one in, and why it buys nothing but
	/// visibility if it leaks.</para>
	///
	/// <para><c>shared: true</c> on the file sink because the Firebase
	/// service writes here too. It runs in this process today, so it is not
	/// strictly needed — but the sink is the one thing that must not itself
	/// become a source of failure, and the cost is a little buffering.</para>
	/// </summary>
	private static void SetupSerilog(MauiAppBuilder builder)
	{
		try
		{
			var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
			Directory.CreateDirectory(logPath);

			// Both feed enrichers, and appName also forms the log filename,
			// so neither may be null. appsettings.json is git-ignored and CI
			// writes a placeholder, so a build without a populated config is
			// a real possibility rather than a theoretical one.
			var appName = builder.Configuration["App:Name"] ?? "Link";
			var environment = builder.Configuration["App:Environment"] ?? "Development";

			// Captured because builder.Configuration is out of scope once DI
			// is built, and the controller rebuilds from this factory.
			var configRef = builder.Configuration;
			_baseLoggerFactory = () => BuildBaseLoggerConfiguration(configRef, logPath, appName, environment);

			Log.Logger = _baseLoggerFactory().CreateLogger();

			// Framework is its own property rather than folded into the
			// message so the aggregator can filter on it directly — the
			// quickest way to tell one runtime from another across a fleet.
			Log.Information(
				"Application {AppName} v{Version} (build {Build}, built {Built}) starting on {Platform} under {Framework}; server {Server}",
				appName,
				BuildInfo.Version,
				BuildInfo.Build,
				BuildInfo.BuildTimestamp,
				DeviceInfo.Platform,
				BuildInfo.Framework,
				LinkServices.Configuration.IsConfigured ? LinkServices.Configuration.BaseUrl : "(not configured)");
		}
#pragma warning disable CA1031 // Deliberately broad: see below.
		catch (Exception)
#pragma warning restore CA1031
		{
			// A logger that cannot be built must not stop the app starting.
			// Serilog's default is a silent logger, so every Log.* call
			// downstream becomes a no-op rather than a null reference —
			// which is exactly the behaviour Link had before any of this
			// existed.
		}
	}

	/// <summary>
	/// A fresh <see cref="LoggerConfiguration"/> holding only the sinks fixed
	/// for the lifetime of the process — file, Debug, and console on desktop
	/// — plus every enricher. The durable Better Stack sink is layered on
	/// separately by <see cref="BetterStackLoggerController"/>, because that
	/// one can be rebuilt at runtime.
	///
	/// Returns a configuration rather than a built logger so the controller
	/// can chain <c>.WriteTo.DurableHttp…</c> before calling
	/// <c>CreateLogger()</c>, giving one pipeline rather than nested ones.
	/// </summary>
	private static LoggerConfiguration BuildBaseLoggerConfiguration(
		IConfiguration config,
		string logPath,
		string appName,
		string environment)
	{
		var cfg = new LoggerConfiguration()
			.ReadFrom.Configuration(config)
			.Enrich.FromLogContext()
			.Enrich.WithProperty("Application", appName)
			.Enrich.WithProperty("Environment", environment)
			.Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
			.Enrich.WithProperty("PlatformVersion", DeviceInfo.VersionString)
			.Enrich.WithProperty("AppVersion", AppInfo.VersionString)
			.Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
			.Enrich.WithProperty("DeviceName", DeviceInfo.Name)
			.Enrich.WithProperty("ProcessId", Environment.ProcessId)
			// Environment.MachineName returns "localhost" on Android and a
			// sandbox name on iOS, so a platform-aware label is what
			// actually distinguishes handsets in a live tail.
			.Enrich.WithProperty("DeviceLabel", ResolveDeviceLabel())
			.Enrich.With<ExceptionEnricher>()
			// Part of the base pipeline, so it survives every reconfigure.
			// See FlushOnErrorSink: an error on a handset in somebody's
			// pocket should not wait for the shipper's timer, or for the
			// member to next open the app.
			.WriteTo.Sink(new FlushOnErrorSink());

#if DEBUG
		cfg = cfg
			.WriteTo.File(
				Path.Combine(logPath, $"{appName.ToLowerInvariant()}-debug-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 21,
				shared: true)
			.WriteTo.Debug();

		// The Serilog console sink calls Console.set_ForegroundColor, which
		// throws PlatformNotSupportedException on Android and iOS — every
		// event then hits SelfLog with a stack trace and drowns the real
		// diagnostics. On mobile the Debug sink already reaches the IDE, so
		// scope this to desktop.
#if WINDOWS || MACCATALYST
		cfg = cfg.WriteTo.Console();
#endif
#else
		cfg = cfg.WriteTo.File(
			Path.Combine(logPath, $"{appName.ToLowerInvariant()}-.log"),
			rollingInterval: RollingInterval.Day,
			retainedFileCountLimit: 7,
			shared: true,
			restrictedToMinimumLevel: LogEventLevel.Information);
#endif

		return cfg;
	}

	/// <summary>
	/// Where the log shipper posts, and what it authenticates with. Empty
	/// unless a build was given one, which is the shipped default.
	/// </summary>
	private static BetterStackConfiguration ReadBetterStackConfiguration(IConfiguration config) =>
		// A scheme-less endpoint is given one by the model's setter rather
		// than here, so every path that sets it is covered by the same rule.
		// See BetterStackConfiguration.Endpoint for what goes wrong without.
		new()
		{
			Endpoint = config["BetterStack:Endpoint"] ?? string.Empty,
			SourceToken = config["BetterStack:SourceToken"] ?? string.Empty,
		};

	/// <summary>
	/// A name for this handset in the live tail. Reads the same preference
	/// Hand does, and falls back to what the platform can say about the
	/// hardware.
	/// </summary>
	private const string DeviceLabelPreferenceKey = "device_label";

	private static string ResolveDeviceLabel()
	{
		try
		{
			var stored = Preferences.Get(DeviceLabelPreferenceKey, string.Empty);
			if (!string.IsNullOrWhiteSpace(stored))
			{
				return stored;
			}
		}
#pragma warning disable CA1031 // Preferences unavailable: fall through to the auto-default.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Nothing to do — the default below is the answer.
		}

		try
		{
			var platform = DeviceInfo.Platform;

			if (platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst)
			{
				var machine = Environment.MachineName;
				if (!string.IsNullOrWhiteSpace(machine)
					&& !string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
				{
					return machine;
				}
			}

			var manufacturer = (DeviceInfo.Manufacturer ?? string.Empty).Trim();
			var model = (DeviceInfo.Model ?? string.Empty).Trim();
			var osName = platform.ToString();
			var osVersion = (DeviceInfo.VersionString ?? string.Empty).Trim();

			var hardware = manufacturer.Length > 0
				&& !model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
				? $"{manufacturer} {model}".Trim()
				: model;

			if (string.IsNullOrWhiteSpace(hardware))
			{
				hardware = "Device";
			}

			return osVersion.Length == 0
				? $"{hardware} ({osName})"
				: $"{hardware} ({osName} {osVersion})";
		}
#pragma warning disable CA1031 // A label is not worth failing a launch for.
		catch (Exception)
#pragma warning restore CA1031
		{
			return "UnknownDevice";
		}
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		// Logging from a crash path must itself be crash-proof. If Log.Fatal
		// throws — a disposed pipeline, an enricher faulting on this
		// particular exception — we must not replace the original crash with
		// a logger crash.

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			try
			{
				if (args.ExceptionObject is Exception ex)
				{
					Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", args.IsTerminating);
				}
				else
				{
					Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", args.ExceptionObject);
				}
			}
#pragma warning disable CA1031 // Never throw from a crash handler.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Nothing to do. The original crash is the story.
			}

			TryFlushLogs();
		};

		// Unobserved Task exceptions. The app usually survives one, so log
		// but do not close.
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			try
			{
				Log.Error(args.Exception, "Unobserved task exception");
			}
#pragma warning disable CA1031 // Never throw from a crash handler.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Nothing to do.
			}

			// Deliberately NOT TryFlushLogs() here. The app survives an
			// unobserved task exception, and CloseAndFlush would leave it
			// running with logging switched off for the rest of the session
			// — trading one silent failure for a worse one. The
			// non-destructive flush ships the event and keeps the pipeline
			// alive.
			try
			{
				BetterStackLoggerController.Current?.Flush();
			}
#pragma warning disable CA1031 // Never throw from a crash handler.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Nothing to do.
			}
		};

#if ANDROID
		// Java-side unhandled exceptions bridged into .NET.
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
		{
			try
			{
				Log.Fatal(args.Exception, "Unhandled Android exception");
			}
#pragma warning disable CA1031 // Never throw from a crash handler.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Nothing to do.
			}

			TryFlushLogs();
		};
#endif
	}

	/// <summary>
	/// Close and flush every Serilog sink with a bounded wait, never
	/// throwing. <c>Log.CloseAndFlush()</c> is synchronous with no timeout;
	/// if the durable sink's final POST is slow or the endpoint unreachable
	/// it can block shutdown for as long as the client's timeout. Anything
	/// still on disk after the cap ships on the next launch — that is the
	/// durable sink's entire purpose.
	/// </summary>
	internal static void TryFlushLogs(TimeSpan? timeout = null)
	{
		try
		{
			Task.Run(Log.CloseAndFlush).Wait(timeout ?? TimeSpan.FromSeconds(5));
		}
#pragma warning disable CA1031 // Never throw from a shutdown or crash path.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Nothing to do.
		}
	}

	private static HttpClient CreateBetterStackHttpClient()
	{
		var handler = new SocketsHttpHandler
		{
			PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};

		return new HttpClient(handler, disposeHandler: true)
		{
			// Fail fast and let the durable sink retry from its on-disk
			// buffer rather than blocking shutdown behind a slow response.
			Timeout = TimeSpan.FromSeconds(30),
		};
	}
}
