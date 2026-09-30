using System.Globalization;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// When this handset last heard from Freedom, as the line under the build
/// on Settings — <c>Config last retrieved 30/09/2026 19:57</c>.
/// </summary>
/// <remarks>
/// <para>For the same person the build line is for: whoever is asking why a
/// handset is not doing what the site says it should. A value changed on
/// the site reaches a handset on its next start, so the question is always
/// when that last was.</para>
///
/// <para>The time is when Freedom last confirmed the handset's copy,
/// whether anything had changed or not, so an up-to-date handset shows
/// today rather than the day a value last moved. UK order, as the suite's
/// dates are.</para>
/// </remarks>
public static class LastRetrieved
{
	public static string Describe(DateTimeOffset? at) =>
		at is { } when
			? "Config last retrieved " + when.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
			: "Config not retrieved yet";
}
