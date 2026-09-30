namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Whether this handset has handed its session to Freedom since Link was
/// last updated.
///
/// <para>A handover is when Freedom learns the handset's name, model and
/// app version, and nothing else ever updates them. So a handset that
/// enrolled on an older build would keep what it sent then — or nothing,
/// for the builds that sent no identity at all — and the site's Devices
/// tab would never catch up. Handing over once more after each update
/// re-attaches the same Freedom device, overrides and all, and brings
/// those details up to date.</para>
/// </summary>
public interface IHandoverRecord
{
	bool IsCurrent { get; }

	void MarkCurrent();
}
