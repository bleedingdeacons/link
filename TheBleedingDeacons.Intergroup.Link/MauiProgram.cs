using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Link.ViewModels;
using TheBleedingDeacons.Intergroup.Link.Views;

namespace TheBleedingDeacons.Intergroup.Link;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Before anything else, so a failure while building the host has
		// somewhere to be recorded.
		SetupSerilog();

		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

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

		Log.Information(
			"Link {Version} starting; server {Server}",
			AppInfo.Current.VersionString,
			LinkServices.Configuration.IsConfigured ? LinkServices.Configuration.BaseUrl : "(not configured)");

		return builder.Build();
	}

	/// <summary>
	/// Serilog, to a rolling file and — in Debug — to the IDE.
	///
	/// <para><b>Why this exists at all.</b> Link shipped without it, and the
	/// first real fault on a handset proved the cost: a pushed message was
	/// stored correctly and the list did not redraw, and the only evidence
	/// available was a screenshot and Android's own log. The app had no
	/// account of itself. Several code paths swallowed exceptions silently
	/// on purpose — a push handler must not throw — and "silently" was
	/// doing more work than it should have been.</para>
	///
	/// <para><b>No Better Stack sink, unlike Hand.</b> That is Hand's
	/// operational concern: a helpline alert that does not ring needs to be
	/// visible to somebody who is not holding the phone. Link's failures
	/// are late messages, and shipping a log-ingestion token in the app to
	/// watch for them would be a credential and a cost for no proportionate
	/// benefit. The file is pulled with adb when somebody is diagnosing.
	/// </para>
	///
	/// <para><c>shared: true</c> because the Firebase service writes here
	/// too. It runs in this process today, so it is not strictly needed —
	/// but the sink is the one thing that must not itself become a source
	/// of failure, and the cost is a little buffering.</para>
	/// </summary>
	private static void SetupSerilog()
	{
		try
		{
			var directory = Path.Combine(FileSystem.AppDataDirectory, "logs");
			Directory.CreateDirectory(directory);

			var configuration = new LoggerConfiguration()
				.Enrich.FromLogContext()
				.WriteTo.File(
					Path.Combine(directory, "link-.log"),
					rollingInterval: RollingInterval.Day,
					retainedFileCountLimit: 7,
					shared: true,
					outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");

#if DEBUG
			configuration = configuration.MinimumLevel.Debug().WriteTo.Debug();
#else
			configuration = configuration.MinimumLevel.Information();
#endif

			Log.Logger = configuration.CreateLogger();
		}
#pragma warning disable CA1031 // Deliberately broad: see below.
		catch (Exception)
#pragma warning restore CA1031
		{
			// A logger that cannot be built must not stop the app starting.
			// Serilog's default is a silent logger, so every Log.* call
			// downstream becomes a no-op rather than a null reference —
			// which is exactly the behaviour Link had before this existed.
		}
	}
}
