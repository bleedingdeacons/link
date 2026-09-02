using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Platforms.Android;

/// <summary>
/// What the push service is allowed to reach for.
///
/// <para>A deliberately small door. <see cref="LinkFirebaseMessagingService"/>
/// runs with no UI and possibly with no MAUI host, so it cannot resolve
/// anything from the app's service provider — but it also should not be
/// free to construct whatever it likes, because the graph it uses has to
/// be the same one the app uses or the two will disagree about where the
/// history lives and which key opens it.</para>
///
/// <para>So there are exactly two things here, both of them delegating to
/// <see cref="LinkServices"/>, which is the single place that says how
/// Link is wired.</para>
/// </summary>
internal static class HeadlessMessages
{
	/// <summary>
	/// The message service, or null when this handset is not signed in.
	///
	/// <para>Checked here rather than left to the service so the push
	/// handler can return early: a push arriving for a signed-out handset
	/// is a stale FCM token at the server, and the right answer is to do
	/// nothing quietly.</para>
	/// </summary>
	public static IMessageService? Resolve()
	{
		var session = LinkServices.Sessions.LoadAsync().GetAwaiter().GetResult();

		return session is null || !session.IsSignedIn ? null : LinkServices.Messages;
	}

	/// <summary>
	/// Tell Fellowship this handset's new FCM registration token.
	///
	/// <para>Built here rather than resolved, for the reason this whole
	/// class exists: there may be no MAUI host. Constructed from
	/// <see cref="LinkServices"/> so it is the same graph the app has —
	/// <see cref="DeviceAuthService"/> holds no state of its own, so a
	/// second one costs nothing and disagrees with the first about
	/// nothing.</para>
	///
	/// <para>Delegating rather than repeating the two lines it takes is
	/// the point. The launch backstop and this rotation callback are the
	/// two ways a token reaches Fellowship, and they must not drift —
	/// this one used to be its own copy, which is how it came to lack the
	/// logging the other has.</para>
	/// </summary>
	public static Task ReportPushToken(string token)
	{
		Log.Information("Firebase issued a new push token; registering it");

		var auth = new DeviceAuthService(
			LinkServices.Client,
			LinkServices.Keys,
			LinkServices.Sessions,
			LinkServices.Push,
			LinkServices.Configuration);

		return auth.RegisterPushTokenAsync(token);
	}
}
