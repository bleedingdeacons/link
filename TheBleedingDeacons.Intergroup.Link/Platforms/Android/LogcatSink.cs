using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Link.Platforms.Android;

/// <summary>
/// Writes Serilog events to Android's log, so <c>adb logcat</c> shows them
/// as they happen.
///
/// <para><b>Why this exists: there was no live log at all.</b> On a handset
/// the pipeline was a rolling file plus <c>WriteTo.Debug()</c>, and the
/// Debug sink goes to <see cref="System.Diagnostics.Debug"/>, which on
/// Android reaches a listening debugger and nothing else. With the app
/// launched by <c>adb</c> rather than from an IDE — which is every
/// deployment <c>/kick</c> makes — that is no listener, so the only record
/// was the file, readable only after the fact with
/// <c>run-as … cat files/logs/…</c>.</para>
///
/// <para>That turned every question into a round trip: reproduce, stop,
/// pull the file, read it, guess again. Three separate diagnoses on
/// 2026-09-10 went that way — an empty address book, a send that failed
/// silently, a sync that never ran — and each would have been one line on
/// a terminal that was already open.</para>
///
/// <para><b>Not the Console sink.</b> Serilog's console sink calls
/// <c>Console.set_ForegroundColor</c>, which throws
/// <see cref="PlatformNotSupportedException"/> on Android; every event then
/// lands in SelfLog with a stack trace and drowns what it was meant to
/// show. MauiProgram has carried a comment about that for as long as the
/// app has existed. This writes through <c>Android.Util.Log</c> instead,
/// which is the platform's own API and colours nothing.</para>
///
/// <para><b>Debug builds only</b>, by where it is registered rather than by
/// anything here. The file sink is already the record; this is for somebody
/// standing over the handset with a cable.</para>
/// </summary>
internal sealed class LogcatSink : ILogEventSink
{
	/// <summary>
	/// The logcat tag, and the thing to filter on:
	/// <c>adb logcat -s Link:V</c>.
	/// </summary>
	/// <remarks>
	/// A tag of our own because the alternative is what Hand ended up
	/// documenting: its output arrives under <c>app_process64</c>, the
	/// runtime's own tag, shared with every other managed message on the
	/// device. Filtering that is filtering the platform.
	/// </remarks>
	public const string Tag = "Link";

	private readonly IFormatProvider? _formatProvider;

	public LogcatSink(IFormatProvider? formatProvider = null) => _formatProvider = formatProvider;

	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		var message = logEvent.RenderMessage(_formatProvider);

		// The exception on its own line rather than folded into the
		// message: logcat wraps on width, and a stack trace appended to a
		// sentence is the shape that gets truncated first.
		if (logEvent.Exception is not null)
		{
			message = message + Environment.NewLine + logEvent.Exception;
		}

		switch (logEvent.Level)
		{
			case LogEventLevel.Verbose:
				global::Android.Util.Log.Verbose(Tag, message);
				break;
			case LogEventLevel.Debug:
				global::Android.Util.Log.Debug(Tag, message);
				break;
			case LogEventLevel.Warning:
				global::Android.Util.Log.Warn(Tag, message);
				break;
			case LogEventLevel.Error:
				global::Android.Util.Log.Error(Tag, message);
				break;
			case LogEventLevel.Fatal:
				// Android has no Fatal that is not an assertion, and Wtf
				// can be configured to kill the process. An app that dies
				// harder because it logged is not a debugging aid.
				global::Android.Util.Log.Error(Tag, message);
				break;
			default:
				global::Android.Util.Log.Info(Tag, message);
				break;
		}
	}
}
