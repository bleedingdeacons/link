using System.Reflection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.Support;
using TheBleedingDeacons.Intergroup.Link.ViewModels;
using TheBleedingDeacons.Intergroup.Link.Views;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Freedom.Client.Maui;
using TheBleedingDeacons.Inventory;
using TheBleedingDeacons.Inventory.Maui;

namespace TheBleedingDeacons.Intergroup.Link;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
#if IOS || MACCATALYST
		// Keep everything SecureStorage writes on the handset that wrote it.
		//
		// MAUI's own default is AfterFirstUnlock, which is included in
		// encrypted device backups and restores onto a different device. For
		// Link that would carry the RSA private key, the history key and the
		// session token onto a handset the intergroup never enrolled — the
		// same outcome AndroidManifest.xml's allowBackup="false" comment
		// describes as precisely what the enrolment flow exists to prevent,
		// and it would make "the private half never leaves the handset"
		// untrue. ThisDeviceOnly has identical unlock semantics, which
		// matters because a push can arrive on a locked phone, and excludes
		// the item from migration.
		//
		// Must be set before anything reads or writes SecureStorage. Items
		// already stored keep the attribute they were written with; those
		// follow at the next enrolment.
		SecureStorage.DefaultAccessible = Security.SecAccessible.AfterFirstUnlockThisDeviceOnly;
#endif

		var builder = MauiApp.CreateBuilder();

		// ── Load appsettings.json from the embedded resource ──────────
		// MAUI does not pick appsettings.json up the way ASP.NET Core does.
		// The file is embedded (see the csproj) and has to be loaded by hand
		// so Serilog's ReadFrom.Configuration actually returns something.
		//
		// The Better Stack settings are not in it, and must not be: they
		// come from Fellowship once the handset has signed in. See
		// RemoteLogging.
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

		// ── Logging ───────────────────────────────────────────────────
		// Inventory (bleedingdeacons/inventory), shared with Register: a
		// rolling file, the IDE and logcat in Debug, the console on desktop,
		// every enricher, the crash handlers for unhandled AppDomain,
		// unobserved-task and Android exceptions, ILogger<T> routed through
		// Serilog, and a log shipper that holds on disk until the site says
		// where to ship. See RemoteLogging.
		//
		// It cannot throw: a logger that will not build leaves Serilog's
		// silent default, and every Log.* call downstream becomes a no-op
		// rather than a null reference.
		//
		// App:Name and App:Environment feed the enrichers and the log file
		// name. appsettings.json is git-ignored and CI writes a placeholder,
		// so a build without them is a real possibility.
		var appName = builder.Configuration["App:Name"] ?? "Link";
		builder.UseInventory(new InventoryMauiOptions
		{
			Application = appName,
			Environment = builder.Configuration["App:Environment"] ?? "Development",
#if DEBUG
			DeveloperSinks = true,
#endif
			// adb -s <serial> logcat -s Link:V
			LogcatTag = "Link",
			// Better Stack gets the same introduction Fellowship does. It is
			// not behind the bot protection this was written for, but a log
			// shipper that names itself is worth having in its own right.
			UserAgent = AppUserAgent.Current(),
			Configure = cfg => cfg.ReadFrom.Configuration(builder.Configuration),
		});

		// Framework is its own property rather than folded into the message
		// so the aggregator can filter on it directly — the quickest way to
		// tell one runtime from another across a fleet.
		Log.Information(
			"Application {AppName} v{Version} (build {Build}, built {Built}) starting on {Platform} under {Framework}; server {Server}",
			appName,
			BuildInfo.Version,
			BuildInfo.Build,
			BuildInfo.BuildTimestamp,
			DeviceInfo.Platform,
			BuildInfo.Framework,
			LinkServices.Configuration.IsConfigured ? LinkServices.Configuration.BaseUrl : "(not configured)");

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
		// Reads the notification permission for the settings indicator and
		// never asks for it — MainActivity does the asking. Not in
		// LinkServices, because that graph exists for the headless push
		// service and nothing without a screen has any use for this.
		builder.Services.AddSingleton<INotificationPermission, NotificationPermission>();

		builder.Services.AddSingleton<IUiDispatcher, MainThreadDispatcher>();
		builder.Services.AddSingleton<IArrivalSound, ArrivalSound>();
		builder.Services.AddSingleton<IRecipientListing, RecipientListing>();
		builder.Services.AddSingleton<IAppleSignIn, AppleSignIn>();
		builder.Services.AddSingleton<DeviceAuthService>();

		// Where the log shipper's settings come from. See RemoteLogging.
		// SecureStorage under the key Link has always used, so a handset
		// upgrading keeps what it was told until the site answers again.
		builder.Services.AddSingleton<ILoggingSettingsStore>(new SecureStorageLoggingSettingsStore("link_logging"));

		// The answer comes from Freedom, on the same site as Fellowship, from
		// its `link` application — signed in to with this handset's
		// Fellowship session, so there is no second Google sign-in. Through
		// LinkServices' HttpClient, the platform's own stack, for the same
		// firewall. A build with no site to talk to never has an answer.
		builder.Services.AddSingleton<ILoggingSource>(sp =>
			Uri.TryCreate(LinkServices.Configuration.BaseUrl, UriKind.Absolute, out var site) && LinkServices.Configuration.IsConfigured
				? new FreedomLoggingSource(new FreedomClientSession(new FreedomClient(
					new FreedomOptions { BaseUrl = site, Application = "link" },
					new SecureStorageFreedomStore("link"),
					new SecureStorageCredentialStore("link"),
					device: new LinkDeviceIdentity(),
					httpClient: LinkServices.Http,
					logger: sp.GetService<ILogger<FreedomClient>>())), new HandoverRecord())
				: NoLoggingSource.Instance);
		builder.Services.AddSingleton<RemoteLogging>();

		builder.Services.AddSingleton<SignInViewModel>();
		builder.Services.AddSingleton<MessagesViewModel>();
		// Transient, unlike the others: one instance per conversation
		// opened. A singleton would keep the previous conversation on
		// screen for the instant before the next one loads, which reads as
		// the wrong one having opened.
		builder.Services.AddTransient<ConversationViewModel>();
		// Transient as well, and it was a singleton until conversations.
		// A singleton kept the last draft's reply pointer, so a new message
		// written after a reply went out answering it — harmless while the
		// list was flat, and filed under the wrong conversation once it was
		// not. A forward has to answer nothing, and a fresh Compose is the
		// only way to be sure it does.
		builder.Services.AddTransient<ComposeViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();

		builder.Services.AddSingleton<SignInPage>();
		builder.Services.AddSingleton<MessagesPage>();
		// Transient for the same reason its view model is.
		builder.Services.AddTransient<ConversationPage>();
		builder.Services.AddTransient<ComposePage>();
		// Transient now that it is pushed rather than a tab, like Compose:
		// a page instance can sit in the navigation stack only once, and a
		// singleton would be the same object pushed on every visit. Its
		// view model stays a singleton, so nothing on it is lost.
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// ── Tell the log shipper where to ship ────────────────────────
		// UseInventory started it holding, because nothing could be read
		// before the container existed. Now it can: whatever the site last
		// handed this handset. Nothing stored — a fresh install, or a
		// handset that has signed out — goes on holding until the site
		// answers; AppShell asks it once the shell is up.
		//
		// Resolved here, not left to the first caller, for the push process
		// too: it has no shell, and the sign-out message RemoteLogging
		// listens for can come from its sync.
		//
		// Blocking on SecureStorage, as LinkServices does for the history
		// key, and for the same reason. It must not stop the app starting.
		try
		{
			app.Services.GetRequiredService<RemoteLogging>().ApplyStoredAsync().GetAwaiter().GetResult();
		}
#pragma warning disable CA1031 // Deliberately broad: logging must not stop a launch.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Stored log settings could not be applied; logging locally only");
		}

		return app;
	}
}
