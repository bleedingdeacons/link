using Serilog.Core;
using Serilog.Events;
using TheBleedingDeacons.Intergroup.Link.Services;

namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Ships the durable buffer as soon as something goes wrong, rather than
/// waiting for the shipper's next tick.
/// </summary>
/// <remarks>
/// <para>The durable sink is disk-first, so an error is never lost — it is
/// written to the buffer as it is emitted and goes out on the next launch
/// at the latest. What this changes is how quickly it <i>arrives</i>.</para>
///
/// <para>That matters because of what the app is. A handset spends its
/// life in someone's pocket; the interesting failures happen while nobody
/// is looking at it, and the process is quite likely to be killed by the
/// OS before it is ever looked at again. Without this, a message that
/// arrived and would not open waits for whenever the member next opens
/// Link — which, if the symptom is that messages stopped appearing, may
/// be a while. With it, the error is on its way within the moment it
/// happened, which is the difference between diagnosing a lost message
/// and guessing at one.</para>
///
/// <para>Deliberately does nothing below <see cref="LogEventLevel.Error"/>.
/// Routine events can wait for the timer; forcing a pipeline rebuild for
/// every informational line would cost far more than it bought.</para>
/// </remarks>
public sealed class FlushOnErrorSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (logEvent.Level < LogEventLevel.Error)
        {
            return;
        }

        var controller = BetterStackLoggerController.Current;
        if (controller is null)
        {
            // Logging starts before the controller is built, so early
            // errors have no one to ask. They are still on disk and ship
            // with the first batch once the sink is attached.
            return;
        }

        // Never inline. Flushing disposes and rebuilds the pipeline, and
        // doing that on the thread that is midway through emitting into it
        // is a deadlock waiting to happen. The controller debounces, so a
        // burst of errors queues one rebuild rather than many.
        _ = Task.Run(() =>
        {
            try
            {
                controller.Flush();
            }
            catch
            {
                // A failure to flush must never escalate: this path exists
                // to report problems, not to become one.
            }
        });
    }
}
