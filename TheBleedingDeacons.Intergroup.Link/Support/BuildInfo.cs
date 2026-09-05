using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Identifies the running build: the app version the operator sees, the
/// build number behind it, and the .NET runtime it is executing on.
///
/// Ported from Hand unchanged apart from its namespace, and deliberately
/// so: the two apps sit on the same handsets, and a member reading a
/// version off one should not have to work out how the other spells it.
///
/// The runtime part is the reason this type exists. An APK on a handset is
/// otherwise indistinguishable from the one it replaced - same icon, same
/// version number, same UI - and "which one is installed here?" is the
/// first question asked whenever a device misbehaves. <see cref="Framework"/>
/// answers it from the device itself, with no cable and no build log.
///
/// Everything is read at runtime rather than baked in at compile time, so
/// there is no build plumbing to keep in step and no way for the reported
/// value to drift from the assembly actually running.
/// </summary>
public static class BuildInfo
{
    /// <summary>
    /// User-facing version, from <c>$(ApplicationDisplayVersion)</c> — e.g. "1.0.26".
    /// </summary>
    public static string Version => AppInfo.VersionString;

    /// <summary>
    /// Build number, from <c>$(ApplicationVersion)</c> — e.g. "1". Distinct from
    /// <see cref="Version"/>: the stores order builds by this, not by the display string.
    /// </summary>
    public static string Build => AppInfo.BuildString;

    /// <summary>
    /// The .NET runtime executing this process — e.g. ".NET 9.0.18" or ".NET 10.0.10".
    /// Reported by the runtime itself, so it reflects what is actually loaded rather
    /// than what the project file asked for.
    /// </summary>
    public static string Framework => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// When this build was produced, in the device's local time, to the second —
    /// e.g. "2026/08/14 05:33.58", or "unknown" if the stamp is absent or malformed.
    /// </summary>
    /// <remarks>
    /// Read from the build metadata of <c>AssemblyInformationalVersion</c>, which the
    /// csproj sets to <c>$(ApplicationDisplayVersion)+yyyyMMddHHmmss</c>. That attribute
    /// is the only one of the three version attributes able to carry a timestamp:
    /// AssemblyVersion and FileVersion are four 16-bit fields capped at 65534 each.
    ///
    /// Stamped UTC, displayed local. Storing UTC keeps builds comparable across a
    /// BST change; converting on the way out means the operator reads a time that
    /// matches the clock in the room, which is the question actually being asked
    /// ("is this the build I just made?"). The raw UTC value stays recoverable from
    /// the assembly attribute.
    ///
    /// Computed once into a static field rather than on each call — it cannot change
    /// while the process lives, and this is read on a page load and at startup.
    ///
    /// Deliberately total: a missing or unparseable stamp yields "unknown" rather than
    /// throwing. This feeds the Settings page and the startup log banner, and neither is
    /// worth crashing for.
    /// </remarks>
    public static string BuildTimestamp { get; } = ReadBuildTimestamp();

    /// <summary>
    /// Everything joined for display and logging — e.g.
    /// "1.0.29 (build 4) · 2026/08/14 03:15.42 · .NET 10.0.11".
    /// Used verbatim by the Settings page and the startup log banner so both always agree.
    /// </summary>
    public static string Summary => $"{Version} (build {Build}) · {BuildTimestamp} · {Framework}";

    private static string ReadBuildTimestamp()
    {
        const string Unknown = "unknown";

        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return Unknown;

        // SemVer build metadata: everything after the first '+'.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0 || plus == informational.Length - 1)
            return Unknown;

        // Then only its leading digits. The csproj disables the SDK's own
        // '+<commit sha>' append, but should anything ever add a segment of its
        // own the stamp we wrote is still the leading run — and taking it means
        // the timestamp survives rather than degrading to "unknown".
        var metadata = informational[(plus + 1)..];
        var length = 0;
        while (length < metadata.Length && char.IsAsciiDigit(metadata[length]))
            length++;

        var stamp = metadata[..length];

        return DateTime.TryParseExact(
            stamp,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var builtUtc)
            ? builtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm.ss", CultureInfo.InvariantCulture)
            : Unknown;
    }
}
