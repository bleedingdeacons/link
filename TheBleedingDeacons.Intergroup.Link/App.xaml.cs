using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services;

namespace TheBleedingDeacons.Intergroup.Link;

public partial class App : Application
{
	/// <summary>How often coming back to the app asks the site which back end to use.</summary>
	private const long BackendCheckMilliseconds = 15 * 60 * 1000;

	private readonly DeviceAuthService _auth;
	private long _backendCheckedAt;

	public App(DeviceAuthService auth, ThemeChoice theme)
	{
		InitializeComponent();

		// Before CreateWindow builds the first page, so nothing is drawn
		// in the empty colours the styles' dynamic resources start with.
		theme.Apply();

		_auth = auth;
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new AppShell()) { Title = "Link" };

	/// <summary>
	/// Re-register this handset's push token at every launch.
	///
	/// <para><b>The backstop for a gap that is otherwise permanent.</b>
	/// Enrolment sends whatever push token Firebase has issued by then, and
	/// deliberately proceeds without one — a handset with no Play Services
	/// must still be able to enrol and collect its messages by polling. But
	/// nothing afterwards ever sent it: <c>OnNewToken</c> fires on
	/// *rotation*, and if it fired before sign-in it found no session and
	/// did nothing. A handset that enrolled in that window stayed poll-only
	/// for good, looking perfectly healthy from both ends.</para>
	///
	/// <para>Fire and forget, and failure is ignored: the worst case is a
	/// handset that keeps polling, which is the state this exists to
	/// improve on rather than a regression. Hand does the same thing for
	/// the same reason.</para>
	/// </summary>
	protected override void OnStart()
	{
		base.OnStart();

		_backendCheckedAt = Environment.TickCount64;

		_ = Task.Run(async () =>
		{
			try
			{
				await _auth.RestoreAsync().ConfigureAwait(false);
			}
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
			catch (Exception ex)
#pragma warning restore CA1031
			{
				Log.Warning(ex, "Push token could not be re-registered at launch");
			}
		});
	}

	/// <summary>
	/// A phone can stay open for days, so coming back to it also checks which back
	/// end to use. Now and then rather than every time, because it is a request to
	/// the site that is almost always answered the same way.
	/// </summary>
	protected override void OnResume()
	{
		base.OnResume();

		if (Environment.TickCount64 - _backendCheckedAt < BackendCheckMilliseconds)
		{
			return;
		}

		_backendCheckedAt = Environment.TickCount64;

		_ = Task.Run(() => _auth.FollowBackendAsync());
	}
}
