using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The real <see cref="IUiDispatcher"/>: MAUI's main thread.
///
/// <para>Trivial, and that is the point. The seam exists so Link.Core can
/// be tested without the MAUI workload, not because there was ever any
/// doubt about what the app should do here.</para>
/// </summary>
public sealed class MainThreadDispatcher : IUiDispatcher
{
	public void Invoke(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		if (MainThread.IsMainThread)
		{
			action();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(action);
		}
	}
}
