using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
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

		return builder.Build();
	}
}
