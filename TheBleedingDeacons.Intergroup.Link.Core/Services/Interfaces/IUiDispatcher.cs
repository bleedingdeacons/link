namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Marshals work onto the UI thread.
///
/// <para>A seam over <c>MainThread</c>, which is MAUI and cannot be
/// referenced from Link.Core. Hand introduced the same one for the same
/// reason: it is what let the alert loop — the app's most important
/// logic — move into a testable library instead of staying welded to the
/// UI framework.</para>
/// </summary>
public interface IUiDispatcher
{
	void Invoke(Action action);
}
