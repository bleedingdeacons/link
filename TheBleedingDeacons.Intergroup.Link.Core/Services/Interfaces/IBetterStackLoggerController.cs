using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// Rebuilds the global Serilog pipeline when Better Stack credentials change.
///
/// Serilog's global <c>Log.Logger</c> is a process-wide singleton that captures
/// its sink configuration at construction time — loggers do not pick up config
/// changes retroactively. When the user edits the Better Stack endpoint or
/// source token in Settings, the previously-built pipeline keeps shipping to
/// the old endpoint with the old token forever unless we explicitly tear it
/// down and rebuild.
///
/// This controller owns that rebuild. It keeps hold of a factory for the
/// "base" pipeline (file / console / debug sinks — the stuff that never
/// changes at runtime) so each reconfigure can compose <c>base + optional
/// Better Stack sink</c> from scratch, dispose the previous pipeline, and swap
/// atomically. That avoids two failure modes the naive approach suffers from:
///
///   • Stacking sinks on every save (old Better Stack sink keeps running,
///     new one added on top, file sink fires twice, etc.).
///   • Leaking the shipper loop inside the durable HTTP sink, which runs a
///     background Timer that would otherwise keep hitting the old endpoint.
/// </summary>
public interface IBetterStackLoggerController
{
	/// <summary>
	/// Rebuild <c>Log.Logger</c> using the supplied Better Stack configuration.
	/// Pass a config whose <c>IsValid()</c> returns <c>false</c> to remove the
	/// Better Stack sink entirely and fall back to local sinks only.
	/// Safe to call from any thread.
	/// </summary>
	void Reconfigure(BetterStackConfiguration config);

	/// <summary>
	/// Ship whatever is sitting in the durable buffer, now, without
	/// tearing logging down.
	/// </summary>
	/// <remarks>
	/// <para>The durable sink writes each event to its on-disk buffer as it
	/// is emitted, so nothing is ever <i>lost</i> — a handset that dies
	/// mid-shift ships its backlog on the next launch. What can be delayed
	/// is <i>arrival</i>: the shipper runs on a timer, so an error can sit
	/// on the phone for the length of that period, and if the process is
	/// killed in between it stays there until the app is next opened. On a
	/// duty handset that is the difference between seeing a fault tonight
	/// and seeing it whenever the responder next happens to sign in.</para>
	///
	/// <para>Distinct from <c>Log.CloseAndFlush()</c>, which also flushes
	/// but leaves the pipeline dead. That is correct on a crash path, where
	/// the process is going away regardless, and wrong everywhere else —
	/// an unobserved task exception does not end the app, and silently
	/// disabling its logging afterwards would be a poor trade. This
	/// disposes the pipeline (which flushes it) and immediately rebuilds
	/// it, so logging continues.</para>
	///
	/// <para>Debounced internally: a burst of errors triggers one flush,
	/// not one per event. Safe to call from any thread, including from
	/// inside a sink.</para>
	/// </remarks>
	void Flush();
}