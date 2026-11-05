using System.Globalization;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// The line on Settings that says when the last backup was. UK order, as the
/// suite's dates are.
/// </summary>
/// <remarks>
/// "Made", because that is all this phone can know. Neither platform's share
/// sheet says whether the file was then saved anywhere.
/// </remarks>
public static class LastBackup
{
	public static string Describe(DateTimeOffset? at) =>
		at is { } when
			? "Last backup made on " + when.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
			: "No backup made on this phone yet";
}
