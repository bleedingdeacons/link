namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Which Firebase project holds this build's messages.
///
/// <para>The API key names the project to Google and proves nothing: what
/// protects the data is the Firestore rules and the device's own sign-in. It
/// comes from the native Firebase configuration on the phone, so it is settable
/// here rather than written in.</para>
/// </summary>
public sealed class FirebaseConfiguration
{
	private string _apiKey = string.Empty;

	public string ProjectId { get; set; } = "fellowship-510917";

	public string Region { get; set; } = "europe-west2";

	public string ApiKey
	{
		get
		{
			if (_apiKey.Length == 0 && ApiKeySource is not null)
			{
				_apiKey = ApiKeySource();
			}

			return _apiKey;
		}

		set => _apiKey = value;
	}

	/// <summary>
	/// Where to read the key from when it's first needed. On iOS the native configuration
	/// isn't loaded until after the app has started building.
	/// </summary>
	public Func<string>? ApiKeySource { get; set; }

	/// <summary>
	/// The headers that say which app is using the key, sent with every call that carries
	/// it. The key only accepts calls from this app, so a call without them is refused.
	/// Empty off the phone.
	/// </summary>
	public IReadOnlyDictionary<string, string> ClientHeaders
	{
		get
		{
			if (_clientHeaders is null)
			{
				_clientHeaders = ClientHeadersSource?.Invoke() ?? new Dictionary<string, string>(StringComparer.Ordinal);
			}

			return _clientHeaders;
		}
	}

	/// <summary>Where to read <see cref="ClientHeaders"/> from, once, when first needed.</summary>
	public Func<IReadOnlyDictionary<string, string>>? ClientHeadersSource { get; set; }

	private IReadOnlyDictionary<string, string>? _clientHeaders;

	public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey);

	/// <summary>The root of this project's documents, with no trailing slash.</summary>
	public string Documents => $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";

	/// <summary>A document's full name, as Firestore's own queries refer to it.</summary>
	public string DocumentName(string path) => $"projects/{ProjectId}/databases/(default)/documents/{path}";

	public Uri Function(string name) => new($"https://{Region}-{ProjectId}.cloudfunctions.net/{name}");

	public Uri Identity(string method) => new($"https://identitytoolkit.googleapis.com/v1/accounts:{method}?key={Uri.EscapeDataString(ApiKey)}");

	public Uri SecureToken => new($"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(ApiKey)}");
}
