namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// Puts <see cref="UserAgent"/> on every request that passes through it.
/// </summary>
/// <remarks>
/// <para>A <see cref="DelegatingHandler"/> rather than
/// <c>DefaultRequestHeaders</c> on the client. Link's own address is
/// embedded in the package and cannot change while the app runs, so the
/// header could in fairness be set once — but Hand's can change, Hand
/// carries this same file, and one shape across the two apps is worth more
/// than the line it saves. It also means the label arrives on the client
/// itself, so the headless push path — which shares
/// <c>LinkServices.Client</c> and has no container to configure — gets it
/// without having to remember to.</para>
///
/// <para>Nothing here throws. A request that cannot be labelled is still a
/// request worth sending, and an exception surfacing from the poll loop is
/// a handset that has silently stopped collecting messages.</para>
/// </remarks>
public sealed class UserAgentHandler : DelegatingHandler
{
	private readonly Func<string> _userAgent;

	/// <summary>
	/// For a client that supplies its own inner handler later, and for
	/// tests.
	/// </summary>
	public UserAgentHandler(Func<string> userAgent)
	{
		_userAgent = userAgent ?? throw new ArgumentNullException(nameof(userAgent));
	}

	/// <summary>
	/// Wraps the platform-native handler the app actually sends through.
	/// See LinkServices for why that handler is not the managed one.
	/// </summary>
	public UserAgentHandler(Func<string> userAgent, HttpMessageHandler innerHandler)
		: base(innerHandler)
	{
		_userAgent = userAgent ?? throw new ArgumentNullException(nameof(userAgent));
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		Apply(request);

		return base.SendAsync(request, cancellationToken);
	}

	/// <summary>
	/// The synchronous path is overridden too, so that a caller reaching
	/// for <see cref="HttpClient.Send(HttpRequestMessage)"/> — as the
	/// headless paths in both apps do — is labelled the same as everything
	/// else.
	/// </summary>
	protected override HttpResponseMessage Send(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		Apply(request);

		return base.Send(request, cancellationToken);
	}

	private void Apply(HttpRequestMessage request)
	{
		string value;

		try
		{
			value = _userAgent();
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Whatever the callback reads — AppInfo, DeviceInfo, the
			// embedded settings — is not worth failing a request over.
			return;
		}

		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		// Cleared first so a retried request, which reuses its message on
		// some handlers, does not end up carrying the string twice.
		request.Headers.UserAgent.Clear();

		// Discarded rather than asserted: UserAgent.ForApp strips what
		// would make this unparseable, so a false here means something
		// upstream changed — and a request with no user-agent is a better
		// answer to that than a throw.
		_ = request.Headers.UserAgent.TryParseAdd(value);
	}
}
