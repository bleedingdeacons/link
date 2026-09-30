using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// <see cref="IHandoverRecord"/> as the app version last handed over, in
/// <see cref="Preferences"/>: nothing to protect, and a string compare on
/// each start.
///
/// <para>Absent on a handset that has never recorded one, which reads as
/// not current. That is what makes every handset enrolled before this
/// existed hand over once and get named.</para>
/// </summary>
public sealed class HandoverRecord : IHandoverRecord
{
	private const string PreferenceKey = "freedom_handover_version";

	public bool IsCurrent =>
		string.Equals(Preferences.Default.Get(PreferenceKey, string.Empty), AppInfo.Current.VersionString, StringComparison.Ordinal);

	public void MarkCurrent() => Preferences.Default.Set(PreferenceKey, AppInfo.Current.VersionString);
}
