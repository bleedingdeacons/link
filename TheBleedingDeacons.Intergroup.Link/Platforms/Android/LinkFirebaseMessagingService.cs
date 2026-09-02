using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Firebase.Messaging;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using AndroidApp = Android.App.Application;

namespace TheBleedingDeacons.Intergroup.Link.Platforms.Android;

/// <summary>
/// Receives Fellowship's push messages.
///
/// <para>This runs with no UI and possibly with no app: Android starts
/// the service to deliver a message even when Link has been swiped away.
/// That is precisely why Fellowship sends <b>data-only</b> messages. A
/// message carrying a <c>notification</c> block would be rendered by the
/// system tray before this code ever ran — which would mean the subject
/// and body travelling through Google in the clear and landing on a lock
/// screen. Everything arrives sealed, this service opens it, and the app
/// decides what the tray says.</para>
///
/// <para><b>What the tray says is deliberately thin.</b> The sender's
/// name and "New message". Not the subject, and never the body. A
/// notification is read by whoever is standing near the phone, and the
/// point of encrypting the payload would be lost if the app then printed
/// it on the lock screen. Someone who wants to read it opens the app.
/// </para>
///
/// <para>The one thing that defeats this entirely is the app being
/// force-stopped, by the user or by an OEM battery manager: a stopped app
/// receives nothing until it is opened again. That is survivable here in
/// a way it is not for Hand — a message is not an alarm — because the
/// next poll collects everything that was missed.</para>
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class LinkFirebaseMessagingService : FirebaseMessagingService
{
	/// <summary>
	/// The one notification channel. Created here rather than at startup
	/// because this service can run before the app has ever been opened
	/// in this process.
	/// </summary>
	public const string ChannelId = "link_messages";

	/// <summary>
	/// How long delivery may take before it is abandoned.
	///
	/// <para>Firebase allows roughly twenty seconds for a high-priority
	/// message before it stops waiting and may kill the process. Ten
	/// leaves room to give up tidily inside that budget. Nothing on this
	/// path makes a network call — the message arrived in the payload —
	/// so ten seconds is an enormous margin over what it takes.</para>
	/// </summary>
	private static readonly TimeSpan DeliveryBudget = TimeSpan.FromSeconds(10);

	public override void OnMessageReceived(RemoteMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);

		base.OnMessageReceived(message);

		try
		{
			var data = message.Data;
			if (data is null || data.Count == 0)
			{
				return;
			}

			if (!data.TryGetValue("k", out var wrappedKey) || !data.TryGetValue("p", out var payload))
			{
				// Not one of ours, or a Fellowship older than this build.
				// Ignored rather than reported: the poll will collect
				// whatever it was.
				return;
			}

			// Resolved on the delivery path rather than cached in a field:
			// this service is created and destroyed by Android at will, so
			// there is no instance lifetime to cache against, and a
			// service resolved once would be one this handset kept using
			// after signing out.
			//
			// Blocking is deliberate. OnMessageReceived has no async form,
			// and returning before the notification is posted is how a
			// message silently never arrives.
			var service = HeadlessMessages.Resolve();
			if (service is null)
			{
				return;
			}

			var stored = service.ReceivePushAsync(wrappedKey, payload)
				.WaitAsync(DeliveryBudget)
				.GetAwaiter()
				.GetResult();

			if (stored is null)
			{
				// The envelope would not open — almost always a keypair
				// this handset has lost. Nothing is shown, because "New
				// message" for something the app cannot display is worse
				// than silence. The next sync reports the key fault, with a
				// session token to hand.
				return;
			}

			Notify(stored);
		}
		catch (TimeoutException)
		{
			// Past the budget. The message is on the server and the next
			// poll will fetch it.
		}
#pragma warning disable CA1031 // Deliberately broad: see below.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Swallowed on purpose. Throwing out of OnMessageReceived kills
			// the process Android started to deliver this message, and the
			// message is already safe on the server — the next poll fetches
			// it. There is nowhere to report to from here that is not itself
			// a thing that can throw.
		}
	}

	/// <summary>
	/// Firebase has issued this handset a new registration token.
	///
	/// <para>Tokens rotate on their own — a restored backup, a cleared app
	/// data, Google's own schedule — and a server pushing to the old one
	/// gets no error worth acting on. So the handset tells Fellowship
	/// rather than waiting to be asked, and until it does, polling covers
	/// the gap.</para>
	/// </summary>
#pragma warning disable S1133 // Deprecated upstream with no replacement in this binding; see the remarks.
	[Obsolete("Overrides a deprecated Firebase callback; see the remarks.")]
#pragma warning restore S1133
	public override void OnNewToken(string token)
	{
		ArgumentNullException.ThrowIfNull(token);

		// Deprecated upstream with no replacement in this binding.
		// Overriding it is the only way to hear about a rotation promptly;
		// the backstop is that DeviceAuthService sends the current token
		// at every enrolment, and the poll covers the gap either way.
#pragma warning disable CS0618
		base.OnNewToken(token);
#pragma warning restore CS0618

		try
		{
			HeadlessMessages.ReportPushToken(token).WaitAsync(DeliveryBudget).GetAwaiter().GetResult();
		}
#pragma warning disable CA1031 // Deliberately broad: see below.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Swallowed for the same reason as above. A token that did not
			// reach Fellowship means pushes go to the old one and stop
			// arriving; polling covers it until the next enrolment or the
			// next rotation.
		}
	}

	/// <summary>
	/// Put the sender's name in the tray, and nothing else.
	/// </summary>
	private static void Notify(LinkMessage message)
	{
		var context = AndroidApp.Context;

		var packageName = context.PackageName;
		if (packageName is null)
		{
			return;
		}

		EnsureChannel();

		var intent = context.PackageManager?.GetLaunchIntentForPackage(packageName);
		intent?.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

		var pending = intent is null
			? null
			: PendingIntent.GetActivity(
				context,
				0,
				intent,
				PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

		// What a lock screen set to hide sensitive content shows instead.
		// Both versions say the same thing, because there is nothing in
		// either one worth hiding — the point of sealing the payload would
		// be lost if the app then printed it in the tray.
		var publicVersion = new NotificationCompat.Builder(context, ChannelId);
		publicVersion.SetContentTitle("Link");
		publicVersion.SetContentText("New message");
		publicVersion.SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail);

		// Written as statements rather than a fluent chain: each call is
		// declared to return a nullable builder, so chaining them makes the
		// analyser right to complain and makes the code wrong to read.
		var builder = new NotificationCompat.Builder(context, ChannelId);
		builder.SetContentTitle(string.IsNullOrEmpty(message.Sender) ? "New message" : message.Sender);
		builder.SetContentText("New message");
		builder.SetSmallIcon(global::Android.Resource.Drawable.SymActionEmail);
		builder.SetAutoCancel(true);
		builder.SetVisibility((int)NotificationVisibility.Private);
		builder.SetPublicVersion(publicVersion.Build());

		if (pending is not null)
		{
			builder.SetContentIntent(pending);
		}

		var notification = builder.Build();
		if (notification is null)
		{
			return;
		}

		// Keyed on the message id, so the same message arriving twice —
		// pushed, then polled — replaces its own notification rather than
		// stacking a second one.
		var manager = NotificationManagerCompat.From(context);
		if (manager is null)
		{
			return;
		}

		manager.Notify((int)(message.Id % int.MaxValue), notification);
	}

	/// <summary>
	/// Create the channel if it is not there.
	///
	/// <para>A channel's importance and sound are fixed when it is created
	/// and cannot be changed afterwards — Hand learned that the expensive
	/// way. Changing either of these later means a new channel id, not an
	/// edit to this one.</para>
	/// </summary>
	private static void EnsureChannel()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			return;
		}

		var context = AndroidApp.Context;
		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);

		if (manager?.GetNotificationChannel(ChannelId) is not null)
		{
			return;
		}

		// Default importance: a message makes a sound and appears in the
		// tray. It does not take the screen over — that is Hand's job for
		// a helpline alert, and doing it for ordinary fellowship business
		// would train people to dismiss both.
		var channel = new NotificationChannel(ChannelId, "Messages", NotificationImportance.Default)
		{
			Description = "Messages from your intergroup",
		};

		manager?.CreateNotificationChannel(channel);
	}
}
