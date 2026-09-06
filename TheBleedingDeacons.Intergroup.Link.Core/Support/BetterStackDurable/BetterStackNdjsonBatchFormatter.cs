using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Http;
using System.Collections.Generic;
using System.IO;

namespace TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable
{
    /// <summary>
    /// Writes a batch of <see cref="LogEvent"/>s as newline-delimited JSON (NDJSON).
    ///
    /// Better Stack's HTTP ingest API accepts a body of <c>application/x-ndjson</c> — one
    /// JSON object per line, no outer array. The per-event JSON itself is produced by
    /// <see cref="BetterStackTextFormatter"/> (that is where the reserved <c>dt</c>,
    /// <c>level</c> and <c>message</c> fields come from); this type only concerns itself
    /// with framing those events into one request body.
    /// </summary>
    public sealed class BetterStackNdjsonBatchFormatter : IBatchFormatter
    {
        public void Format(IEnumerable<string> logEvents, TextWriter output)
        {
            // When events are replayed from the durable buffer they arrive pre-rendered as
            // JSON strings — one per line. We just pass them through.
            if (logEvents == null) return;

            foreach (var logEvent in logEvents)
            {
                if (string.IsNullOrWhiteSpace(logEvent)) continue;

                output.WriteLine(logEvent);
            }
        }

#pragma warning disable S2325 // Implements IBatchFormatter; it cannot be static.
        public void Format(IEnumerable<LogEvent> logEvents, ITextFormatter formatter, TextWriter output)
        {
            // Not used by the durable sink — it always calls the string overload above,
            // because events are persisted as formatted JSON in the rolling buffer first.
            // Implemented for completeness against the interface contract.
            if (logEvents == null || formatter == null) return;

            foreach (var logEvent in logEvents)
            {
                // Render via a buffer so we can normalise the line ending: the configured
                // formatter may or may not terminate the event itself, and NDJSON needs
                // exactly one newline per event.
                var buffer = new StringWriter();
                formatter.Format(logEvent, buffer);

                var json = buffer.ToString().TrimEnd('\r', '\n');
                if (json.Length == 0) continue;

                output.WriteLine(json);
            }
        }
#pragma warning restore S2325
    }
}
