using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Reads — never asks for — this phone's notification permission.
///
/// <para>Partial, with one half per head, the same arrangement as
/// <see cref="PushRegistrar"/>. The shared half owns the one rule both
/// platforms obey: a read that throws answers false rather than
/// propagating. This exists to paint an indicator, and an indicator that
/// took the settings screen down with it would be worse than none.</para>
/// </summary>
public sealed partial class NotificationPermission : INotificationPermission
{
	public async Task<bool> IsGrantedAsync()
	{
		try
		{
			return await PlatformIsGrantedAsync().ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception)
#pragma warning restore CA1031
		{
			return false;
		}
	}

	private partial Task<bool> PlatformIsGrantedAsync();
}
