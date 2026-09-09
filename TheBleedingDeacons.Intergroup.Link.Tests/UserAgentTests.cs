using System.Net;
using TheBleedingDeacons.Intergroup.Link.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Link.Tests;

/// <summary>
/// The header every outbound request introduces itself with.
///
/// <para>The shape asserted here is the one an upstream's bot protection
/// asked Fellowship for — product, version, contact, deployment — with the
/// head added in front. These tests are deliberately literal about the
/// punctuation, and their counterparts in
/// <c>fellowship/tests/UserAgentTest.php</c> and in Hand assert the same
/// one. If a change here reads as an improvement, it is a change to all
/// three.</para>
/// </summary>
public sealed class UserAgentTests
{
	private const string Server = "https://aa-bristol.org";

	private const string Expected = "Link/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)";

	[Fact]
	public void ItNamesTheApp_ItsVersion_TheHead_AContact_AndTheServer()
	{
		Assert.Equal(
			"Link/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Link", "1.2.3", "Android", Server));
	}

	[Fact]
	public void AMissingVersionLeavesOutTheSlash()
	{
		// Better a product with no version than "Link/" or an invented one.
		Assert.Equal(
			"Link (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Link", string.Empty, "Android", Server));
	}

	[Fact]
	public void AMissingPlatformLeavesOutItsToken()
	{
		// Which is the plugins' string exactly, for anything that cannot
		// tell which head it is running on.
		Assert.Equal(
			"Link/1.2.3 (rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Link", "1.2.3", string.Empty, Server));
	}

	[Fact]
	public void AnEmptyAppNameFallsBackToTheProduct()
	{
		Assert.StartsWith("Link/1.0", UserAgent.ForApp(string.Empty, "1.0", "iOS", Server), StringComparison.Ordinal);
	}

	[Fact]
	public void AServerThatIsNotKnownYetReadsAsUnknown()
	{
		// A build shipped without usable settings, which LinkServices
		// answers with an unconfigured object rather than throwing. Saying
		// so beats a dangling semicolon.
		Assert.Equal(
			"Link/1.2.3 (Android; rest@aa-bristol.org; unknown)",
			UserAgent.ForApp("Link", "1.2.3", "Android", string.Empty));
	}

	[Fact]
	public void ATrailingSlashOnTheServerIsDropped()
	{
		// The two spellings of a site root are one deployment, and should
		// not read as two in an access log.
		Assert.Equal(
			UserAgent.ForApp("Link", "1.2.3", "Android", Server),
			UserAgent.ForApp("Link", "1.2.3", "Android", Server + "/"));
	}

	[Fact]
	public void HeaderBreakingCharactersAreStripped()
	{
		// A newline here would be header injection; a bracket or semicolon
		// would close the comment early and leave the contact details
		// dangling outside it.
		Assert.Equal(
			"Widget/1.2.3 evil (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Widget", "1.2.3\r\n(evil);", "Android", Server));
	}

	[Fact]
	public void TheContactIsTheRoleAddress()
	{
		// Not a personal one: it outlives whoever is maintaining this.
		Assert.Equal("rest@aa-bristol.org", UserAgent.Contact);
	}

	[Fact]
	public void TheHandlerRefusesANullCallback()
	{
		Assert.Throws<ArgumentNullException>(() => new UserAgentHandler(null!));
		Assert.Throws<ArgumentNullException>(() => new UserAgentHandler(null!, new RecordingHandler()));
	}

	[Fact]
	public async Task TheHandlerLabelsEveryRequest()
	{
		var (client, recorder) = Client(() => Expected);

		await client.GetAsync(new Uri("https://aa-bristol.org/one"));
		await client.GetAsync(new Uri("https://aa-bristol.org/two"));

		Assert.Equal([Expected, Expected], recorder.UserAgents);
	}

	[Fact]
	public async Task TheHandlerFollowsTheServerAddressChanging()
	{
		// The string is built per request rather than captured once.
		// Link's own address is embedded and cannot move, but Hand carries
		// the same handler and its address can, so the property is
		// asserted on both sides rather than only where it bites.
		var server = "https://aa-bristol.org";
		var (client, recorder) = Client(() => UserAgent.ForApp("Link", "1.2.3", "Android", server));

		await client.GetAsync(new Uri("https://aa-bristol.org/one"));
		server = "https://test.aa-bristol.org";
		await client.GetAsync(new Uri("https://test.aa-bristol.org/two"));

		Assert.Equal(
			[
				"Link/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)",
				"Link/1.2.3 (Android; rest@aa-bristol.org; https://test.aa-bristol.org)",
			],
			recorder.UserAgents);
	}

	[Fact]
	public async Task TheHandlerReplacesAHeaderTheRequestAlreadyCarried()
	{
		// A retried request can arrive here having been through once
		// already, and two product tokens is not what anybody meant.
		var (client, recorder) = Client(() => Expected);

		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://aa-bristol.org/one"));
		request.Headers.UserAgent.ParseAdd("Something/0.1");

		await client.SendAsync(request);

		Assert.Equal([Expected], recorder.UserAgents);
	}

	[Fact]
	public async Task ACallbackThatThrowsDoesNotFailTheRequest()
	{
		// Whatever it reads — AppInfo, DeviceInfo, the embedded settings —
		// is not worth losing a message over.
		var (client, recorder) = Client(() => throw new InvalidOperationException("no MAUI here"));

		var response = await client.GetAsync(new Uri("https://aa-bristol.org/one"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal([string.Empty], recorder.UserAgents);
	}

	[Fact]
	public void TheSynchronousPathIsLabelledToo()
	{
		// Nothing in Link reaches for HttpClient.Send today. Hand's
		// headless alert reporter does — no app, no thread to await on —
		// and that request would go out unlabelled if only SendAsync were
		// overridden. Asserted here too so the two copies stay one file.
		var (client, recorder) = Client(() => Expected);

		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://aa-bristol.org/one"));
		using var response = client.Send(request);

		Assert.Equal([Expected], recorder.UserAgents);
	}

	private static (HttpClient Client, RecordingHandler Recorder) Client(Func<string> userAgent)
	{
		var recorder = new RecordingHandler();

		return (new HttpClient(new UserAgentHandler(userAgent, recorder)), recorder);
	}

	/// <summary>
	/// Answers everything with 200 and remembers what each request said it
	/// was. Both send paths, because the handler overrides both.
	/// </summary>
	private sealed class RecordingHandler : HttpMessageHandler
	{
		public List<string> UserAgents { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(Respond(request));

		protected override HttpResponseMessage Send(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			Respond(request);

		private HttpResponseMessage Respond(HttpRequestMessage request)
		{
			UserAgents.Add(request.Headers.UserAgent.ToString());

			return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
		}
	}
}
