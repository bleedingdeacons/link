using System.Net;
using Microsoft.Extensions.Configuration;
using Serilog.Sinks.Http;

namespace TheBleedingDeacons.Intergroup.Link.Support.BetterStackDurable;

/// <summary>
/// An <see cref="IHttpClient"/> that sends nothing and says so.
///
/// <para>Used while a handset has not yet been told where to ship: the
/// durable sink still writes every event to its buffer, and every attempt
/// to ship a batch is answered with a 503, which the sink reads as "keep
/// it and try later". When the intergroup does answer, the real client
/// replaces this one over the same buffer files, and what was held goes
/// out with its original timestamps. See <c>RemoteLogging</c>.</para>
///
/// <para>Makes no network call at all. There is nowhere to call.</para>
/// </summary>
public sealed class HoldingHttpClient : IHttpClient
{
	public void Configure(IConfiguration configuration)
	{
		// Nothing to configure.
	}

	public Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
		{
			ReasonPhrase = "Holding until the intergroup says where to ship",
			Content = new StringContent(string.Empty),
		});

	public void Dispose()
	{
		// Nothing held.
	}
}
