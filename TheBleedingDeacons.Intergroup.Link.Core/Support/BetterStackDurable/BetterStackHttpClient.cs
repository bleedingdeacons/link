using Microsoft.Extensions.Configuration;
using Serilog.Sinks.Http;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable
{
	/// <summary>
	/// <see cref="IHttpClient"/> implementation used by Serilog.Sinks.Http's durable sink
	/// to POST batches of NDJSON log events to the Better Stack ingest endpoint.
	///
	/// Responsibilities:
	///  • Attach the Bearer source token to every request (Better Stack requires it).
	///  • Tag the content as application/x-ndjson, matching what <see cref="BetterStackNdjsonBatchFormatter"/>
	///    writes — Better Stack parses one JSON event per line.
	///  • Reuse the app's platform-native <see cref="HttpClient"/> so log uploads use the same TLS
	///    stack as the rest of the app (see <c>MauiProgram.CreateHttpClient</c> for the WAF-fingerprint
	///    rationale — if API calls can get through, so can log uploads).
	///
	/// The durable sink calls <see cref="PostAsync(string, Stream, CancellationToken)"/>. If the POST
	/// fails (offline, 5xx, TLS error, etc.) the sink keeps the batch on disk and retries later.
	/// No log events are lost as long as the buffer file survives, which it does across process
	/// kills — that is the whole point.
	/// </summary>
	public sealed class BetterStackHttpClient : IHttpClient
	{
		private readonly HttpClient _httpClient;
		private readonly string _sourceToken;
		private readonly bool _ownsHttpClient;

		public BetterStackHttpClient(string sourceToken, HttpClient httpClient)
		{
			_sourceToken = sourceToken ?? throw new ArgumentNullException(nameof(sourceToken));
			_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			_ownsHttpClient = false;
		}

		/// <summary>
		/// Serilog.Sinks.Http calls this when the sink is configured via appsettings.json.
		/// We configure ourselves via constructor injection, so this is a no-op.
		/// </summary>
		public void Configure(IConfiguration configuration)
		{
			// Intentionally empty — see summary.
		}

		public async Task<HttpResponseMessage> PostAsync(
			string requestUri,
			Stream contentStream,
			CancellationToken cancellationToken)
		{
			// IMPORTANT: this method must never throw. The durable sink calls us
			// from a background shipper loop AND from its Dispose path (via
			// flushOnClose: true, which your configuration relies on). An
			// exception from the dispose path propagates out of Log.CloseAndFlush()
			// and into application shutdown code — the symptom you just observed
			// as "invalid response from server" surfacing at window-destroy time.
			//
			// The sink's contract is simpler than it looks: it needs an
			// HttpResponseMessage. A non-2xx status tells it "I couldn't ship
			// this batch, please keep it on the buffer file and try again
			// later." That's exactly what we want on a transport failure.
			// We map any exception to 599 (a conventional unofficial status
			// meaning "network connect failure") and log the real cause to
			// Serilog's SelfLog so it's diagnosable without leaking into the app.
			try
			{
				using var content = new StreamContent(contentStream);

				// Better Stack accepts NDJSON (one event per line) or a JSON array. We use NDJSON
				// because it streams well from a buffer file and the per-event framing means a
				// single malformed row cannot poison an entire batch.
				content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");

				using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
				{
					Content = content
				};
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sourceToken);

				return await _httpClient
					.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// Legitimate cancellation — surface it via SelfLog but still
				// return a retryable response so the sink keeps the batch.
				Serilog.Debugging.SelfLog.WriteLine(
					"[BetterStackHttpClient] POST cancelled; batch retained for retry.");
				return CreateTransportFailureResponse("Request cancelled");
			}
			catch (Exception ex)
			{
				// Everything else: timeouts, DNS failures, TLS errors,
				// WinHttpException 12152, a disposed HttpClient on shutdown.
				// The sink MUST NOT see this as a throw.
				Serilog.Debugging.SelfLog.WriteLine(
					"[BetterStackHttpClient] POST to {0} failed ({1}): {2}. Batch retained for retry.",
					requestUri, ex.GetType().Name, ex.Message);
				return CreateTransportFailureResponse(ex.Message);
			}
		}

		/// <summary>
		/// Builds a minimal, disposable <see cref="HttpResponseMessage"/> with a
		/// non-2xx status so the durable sink treats the batch as unshipped and
		/// retries on its next tick. 599 is a conventional unofficial status for
		/// client-side network failure.
		/// </summary>
		private static HttpResponseMessage CreateTransportFailureResponse(string reason)
		{
			return new HttpResponseMessage((System.Net.HttpStatusCode)599)
			{
				ReasonPhrase = reason,
				Content = new StringContent(string.Empty),
			};
		}

		public void Dispose()
		{
			// We do not own the HttpClient — it is the app-wide singleton registered in DI.
			// Disposing it here would kill the HttpClient used for every other outbound call.
			if (_ownsHttpClient)
			{
				_httpClient.Dispose();
			}
		}
	}
}