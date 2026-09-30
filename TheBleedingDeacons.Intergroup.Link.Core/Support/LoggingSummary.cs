using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Where this handset's logs go, as the lines under the build on the
/// Settings screen — <c>Better Stack: shipping</c>, the endpoint, and
/// whether a source token is held.
/// </summary>
/// <remarks>
/// <para>For the same person the build line is for: whoever is asking why a
/// handset's logs have not arrived. Freedom sets both values from the
/// intergroup's site, so the handset is the only place that can say what it
/// was actually given and whether it is using it.</para>
///
/// <para><b>The token is never shown</b>, only whether there is one. It is
/// a write credential for the log source, and a screenshot of Settings is
/// exactly what gets sent to somebody when a phone misbehaves.</para>
/// </remarks>
public static class LoggingSummary
{
	public static string Describe(BetterStackConfiguration? configuration, ShippingState state)
	{
		var endpoint = configuration?.Endpoint ?? string.Empty;
		var hasToken = !string.IsNullOrWhiteSpace(configuration?.SourceToken);

		return string.Join(
			Environment.NewLine,
			"Better Stack: " + Describe(state),
			endpoint.Length > 0 ? endpoint : "No endpoint",
			hasToken ? "Source token set" : "No source token");
	}

	private static string Describe(ShippingState state) => state switch
	{
		ShippingState.Shipping => "shipping",
		ShippingState.Holding => "holding logs until it is told where to ship",
		ShippingState.LocalOnly => "logs stay on this phone",
		_ => "not started",
	};
}
