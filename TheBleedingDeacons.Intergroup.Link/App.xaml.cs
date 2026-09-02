using Serilog;
using TheBleedingDeacons.Intergroup.Link.Services;

namespace TheBleedingDeacons.Intergroup.Link;

public partial class App : Application
{
	private readonly DeviceAuthService _auth;

	public App(DeviceAuthService auth)
	{
		InitializeComponent();

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
}
