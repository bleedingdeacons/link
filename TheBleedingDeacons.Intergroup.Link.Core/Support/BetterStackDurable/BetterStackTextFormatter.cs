using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using System;
using System.Globalization;
using System.IO;

namespace TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable
{
	/// <summary>
	/// Serialises a single <see cref="LogEvent"/> into the JSON shape Better Stack's
	/// ingest API actually understands, one event per line (NDJSON).
	///
	/// <para>Better Stack treats three field names as reserved and ignores everything else
	/// as free-form structured metadata:</para>
	/// <list type="bullet">
	/// <item><c>dt</c> — the event timestamp. <b>Without it Better Stack stamps the event with
	///       the time the HTTP request arrived.</b> That is the reason this formatter exists:
	///       the durable sink buffers events on disk, so a phone that has been off signal for
	///       hours ships a batch whose events would otherwise all collapse onto the delivery
	///       moment and lose the real chronology of the meeting.</item>
	/// <item><c>level</c> — severity, used for filtering and the colour coding in Live Tail.</item>
	/// <item><c>message</c> — the human-readable text shown in Live Tail. Without it the tail
	///       shows the raw JSON blob for every row.</item>
	/// </list>
	///
	/// <para>Serilog's stock <see cref="JsonFormatter"/> emits <c>Timestamp</c>/<c>Level</c>/
	/// <c>RenderedMessage</c> instead, none of which Better Stack recognises — which is what
	/// this type replaces. The field layout below mirrors Better Stack's own
	/// <c>BetterStack.Logs.Serilog</c> client so the events look identical to what their
	/// documented .NET integration produces.</para>
	///
	/// <para>Enriched properties are nested under <c>properties</c> rather than hoisted to the
	/// top level, again matching the official client, and guaranteeing an enricher can never
	/// shadow <c>dt</c>, <c>level</c> or <c>message</c>. Query them in Better Stack as
	/// <c>properties.DeviceLabel</c>, <c>properties.ExceptionType</c>, and so on.</para>
	/// </summary>
	public sealed class BetterStackTextFormatter : ITextFormatter
	{
		private readonly JsonValueFormatter _valueFormatter = new();

		public void Format(LogEvent logEvent, TextWriter output)
		{
			if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
			if (output == null) throw new ArgumentNullException(nameof(output));

			// Format into a buffer first. The durable sink writes this straight into the
			// rolling buffer file and reads it back a line at a time, so a half-written
			// event would corrupt that row (and, since it is NDJSON, only that row —
			// but there is no reason to ship a broken one at all). Building the whole
			// line before touching the output stream makes the write all-or-nothing.
			try
			{
				var buffer = new StringWriter(CultureInfo.InvariantCulture);
				FormatContent(logEvent, buffer);
				output.WriteLine(buffer.ToString());
			}
			catch (Exception ex)
			{
				// Dropping one event beats taking down the shipper loop. SelfLog is wired
				// to Debug output in BetterStackLoggerController.
				SelfLog.WriteLine(
					"[BetterStackTextFormatter] Event at {0} with template {1} could not be serialised and was dropped: {2}",
					logEvent.Timestamp.ToString("o", CultureInfo.InvariantCulture),
					logEvent.MessageTemplate.Text,
					ex);
			}
		}

		private void FormatContent(LogEvent logEvent, TextWriter output)
		{
			// Better Stack accepts RFC 3339 / ISO 8601; "o" on a UTC DateTime gives
			// 2026-08-10T19:04:31.1234567Z, which parses exactly.
			output.Write("{\"dt\":\"");
			output.Write(logEvent.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));

			output.Write("\",\"level\":\"");
			output.Write(MapLevel(logEvent.Level));

			output.Write("\",\"message\":");
			JsonValueFormatter.WriteQuotedJsonString(
				logEvent.MessageTemplate.Render(logEvent.Properties, CultureInfo.InvariantCulture),
				output);

			// Keeping the raw template alongside the rendered text means events of the
			// same kind can still be grouped in Better Stack even though the rendered
			// message differs per occurrence.
			output.Write(",\"messageTemplate\":");
			JsonValueFormatter.WriteQuotedJsonString(logEvent.MessageTemplate.Text, output);

			if (logEvent.Exception != null)
			{
				output.Write(",\"exception\":");
				JsonValueFormatter.WriteQuotedJsonString(logEvent.Exception.ToString(), output);
			}

			if (logEvent.Properties.Count != 0)
			{
				output.Write(",\"properties\":{");

				var delimiter = string.Empty;
				foreach (var property in logEvent.Properties)
				{
					output.Write(delimiter);
					delimiter = ",";

					JsonValueFormatter.WriteQuotedJsonString(property.Key, output);
					output.Write(':');
					_valueFormatter.Format(property.Value, output);
				}

				output.Write('}');
			}

			output.Write('}');
		}

		/// <summary>
		/// Serilog's level names are not all names Better Stack recognises. Their own client
		/// folds Information to INFO; Verbose and Warning are likewise Serilog-specific
		/// spellings, so they map onto the conventional TRACE/WARN that every log platform
		/// (Better Stack included) understands. Debug, Error and Fatal pass through unchanged.
		/// </summary>
		private static string MapLevel(LogEventLevel level) => level switch
		{
			LogEventLevel.Verbose => "TRACE",
			LogEventLevel.Information => "INFO",
			LogEventLevel.Warning => "WARN",
			_ => level.ToString().ToUpperInvariant(),
		};
	}
}
