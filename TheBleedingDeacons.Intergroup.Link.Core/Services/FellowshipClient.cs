using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Talks to the Fellowship plugin over HTTPS.
///
/// <para><b>Every method answers a value, never an exception.</b> A
/// messaging app on a phone spends a good deal of its life with no
/// network, and there is nothing any caller here would do with an
/// <see cref="HttpRequestException"/> except catch it and carry on. So
/// the catching happens once, here, and the callers get "it did not
/// work" in whatever shape suits them — a null, a false, an
/// <see cref="InboxPage.Failed"/>.</para>
///
/// <para>The one distinction worth preserving is between "the server
/// refused and said why" and "the request did not arrive". The first is
/// something to show a member — "that address does not match a member
/// record" is the whole of what somebody needs to know at that moment —
/// and the second is not, because it will fix itself.</para>
/// </summary>
public sealed class FellowshipClient : IFellowshipClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly FellowshipConfiguration _configuration;

	/// <summary>
	/// The <see cref="HttpClient"/> is injected rather than constructed so
	/// a test can hand in a stub handler. That is the only reason.
	/// </summary>
	public FellowshipClient(HttpClient http, FellowshipConfiguration configuration)
	{
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	public async Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default)
	{
		var query = $"auth/device/start?provider={Uri.EscapeDataString(provider ?? string.Empty)}"
			+ $"&redirect_uri={Uri.EscapeDataString(_configuration.CallbackUrl)}";

		var json = await GetAsync(_configuration.Route(query), token: null, cancellationToken).ConfigureAwait(false);
		if (json is null)
		{
			return null;
		}

		var state = Text(json.Value, "state");
		if (string.IsNullOrEmpty(state))
		{
			return null;
		}

		return new SignInStart
		{
			State = state,
			AuthorizationUrl = Text(json.Value, "authorization_url"),
			Nonce = Text(json.Value, "nonce"),
		};
	}

	public async Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["public_key"] = request.PublicKey,
			["platform"] = request.Platform,
			["label"] = request.Label,
			["push_provider"] = request.PushProvider,
			["push_token"] = request.PushToken,
		};

		// One credential shape, never two. The server refuses a request
		// carrying none, and sending an empty one alongside a real one
		// would look to it like the wrong flow.
		//
		// The password path is a different route rather than a third
		// branch of the same one: it is the only shape where this app
		// hands over a secret rather than relaying a proof, and the
		// server rate-limits and audits it accordingly.
		var route = "auth/device/exchange";

		if (!string.IsNullOrEmpty(request.Password))
		{
			route = "auth/device/password";
			body["email"] = request.Email;
			body["password"] = request.Password;
		}
		else if (!string.IsNullOrEmpty(request.Code))
		{
			body["code"] = request.Code;
		}
		else
		{
			body["state"] = request.State;
			body["id_token"] = request.IdToken;
		}

		using var response = await PostAsync(route, token: null, body, cancellationToken).ConfigureAwait(false);
		if (response is null)
		{
			return EnrolmentResult.Failed("Could not reach the intergroup. Check your connection and try again.");
		}

		var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			// The server's own message, where it gave one. These are
			// written to be read by a member — see Fellowship's
			// DeviceAuthController — and flattening them into "sign-in
			// failed" would throw away the only actionable thing on the
			// screen.
			var message = json is null ? string.Empty : Text(json.Value, "message");

			return EnrolmentResult.Failed(string.IsNullOrEmpty(message)
				? "This device could not be enrolled. Please try again."
				: message);
		}

		if (json is null)
		{
			return EnrolmentResult.Failed("The intergroup answered something this app could not read.");
		}

		var token = Text(json.Value, "token");
		if (string.IsNullOrEmpty(token))
		{
			return EnrolmentResult.Failed("The intergroup did not issue a device token.");
		}

		var device = Child(json.Value, "device");
		var member = Child(json.Value, "member");

		return EnrolmentResult.Ok(new DeviceSession
		{
			Token = token,
			DeviceId = device is null ? 0 : Number(device.Value, "id"),
			MemberId = member is null ? 0 : Number(member.Value, "id"),
			MemberName = member is null ? string.Empty : Text(member.Value, "name"),
		});
	}

	public async Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["email"] = email ?? string.Empty,
		};

		using var response = await PostAsync("auth/password/request", token: null, body, cancellationToken).ConfigureAwait(false);

		// A false here means the request never arrived, not that the
		// address was refused — the server answers 200 either way, on
		// purpose.
		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<PasswordSetResult> SetPasswordAsync(
		string code,
		string password,
		CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["token"] = code ?? string.Empty,
			["password"] = password ?? string.Empty,
		};

		using var response = await PostAsync("auth/password/complete", token: null, body, cancellationToken).ConfigureAwait(false);
		if (response is null)
		{
			return PasswordSetResult.Failed("Could not reach the intergroup. Check your connection and try again.");
		}

		if (response.IsSuccessStatusCode)
		{
			return PasswordSetResult.Ok();
		}

		var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		var message = json is null ? string.Empty : Text(json.Value, "message");

		// The server's own wording. It distinguishes an expired code from
		// a password its policy refuses, and those need different things
		// from whoever is reading them.
		return PasswordSetResult.Failed(string.IsNullOrEmpty(message)
			? "That did not work. Please ask for a new link and try again."
			: message);
	}

	public async Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default)
	{
		var query = "messages?since=" + sinceId.ToString(CultureInfo.InvariantCulture);

		var json = await GetAsync(_configuration.Route(query), token, cancellationToken).ConfigureAwait(false);
		if (json is null)
		{
			// Distinct from an empty page. A caller that conflated the two
			// would clear its unread badge every time the network dropped.
			return InboxPage.Failed;
		}

		var sealedMessages = new List<SealedMessage>();

		if (json.Value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in messages.EnumerateArray())
			{
				var id = Number(element, "id");
				var wrappedKey = Text(element, "k");
				var payload = Text(element, "p");

				if (id <= 0 || string.IsNullOrEmpty(wrappedKey) || string.IsNullOrEmpty(payload))
				{
					continue;
				}

				sealedMessages.Add(new SealedMessage { Id = id, WrappedKey = wrappedKey, Payload = payload });
			}
		}

		return new InboxPage
		{
			Messages = sealedMessages,
			Unread = (int)Number(json.Value, "unread"),
		};
	}

	public async Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default)
	{
		var route = "messages/" + messageId.ToString(CultureInfo.InvariantCulture) + "/read";

		using var response = await PostAsync(route, token, body: null, cancellationToken).ConfigureAwait(false);

		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var body = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["subject"] = request.Subject,
			["body"] = request.Body,
		};

		if (request.MemberIds.Count > 0)
		{
			body["member_ids"] = request.MemberIds;
		}

		if (!string.IsNullOrEmpty(request.Committee))
		{
			body["committee"] = request.Committee;
		}

		if (request.ReplyToId > 0)
		{
			body["reply_to"] = request.ReplyToId;
		}

		using var response = await PostAsync("messages", token, body, cancellationToken).ConfigureAwait(false);
		if (response is null)
		{
			return SendResult.Failed("Could not reach the intergroup. The message has not been sent.");
		}

		var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			var message = json is null ? string.Empty : Text(json.Value, "message");

			return SendResult.Failed(string.IsNullOrEmpty(message)
				? "The message could not be sent."
				: message);
		}

		if (json is null)
		{
			return SendResult.Failed("The intergroup answered something this app could not read.");
		}

		return new SendResult
		{
			MessageId = Number(json.Value, "id"),
			Recipients = (int)Number(json.Value, "recipients"),
		};
	}

	public async Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default)
	{
		var json = await GetAsync(_configuration.Route("directory"), token, cancellationToken).ConfigureAwait(false);
		if (json is null)
		{
			return FellowshipDirectory.Empty;
		}

		var members = new List<DirectoryMember>();
		var committees = new List<DirectoryCommittee>();

		if (json.Value.TryGetProperty("members", out var memberArray) && memberArray.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in memberArray.EnumerateArray())
			{
				var id = Number(element, "id");
				if (id > 0)
				{
					members.Add(new DirectoryMember { Id = id, Name = Text(element, "name") });
				}
			}
		}

		if (json.Value.TryGetProperty("committees", out var committeeArray) && committeeArray.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in committeeArray.EnumerateArray())
			{
				var slug = Text(element, "slug");
				if (!string.IsNullOrEmpty(slug))
				{
					committees.Add(new DirectoryCommittee
					{
						Slug = slug,
						Name = Text(element, "name"),
						ParentId = Number(element, "parent"),
					});
				}
			}
		}

		return new FellowshipDirectory { Members = members, Committees = committees };
	}

	public async Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["push_provider"] = "fcm",
			["push_token"] = pushToken ?? string.Empty,
		};

		using var response = await PostAsync("auth/device/push", token, body, cancellationToken).ConfigureAwait(false);

		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<bool> RotateKeyAsync(string token, string publicKey, CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["public_key"] = publicKey ?? string.Empty,
		};

		using var response = await PostAsync("auth/device/key", token, body, cancellationToken).ConfigureAwait(false);

		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default)
	{
		using var response = await PostAsync("auth/device/key-fault", token, body: null, cancellationToken).ConfigureAwait(false);

		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Delete, _configuration.Route("auth/device"));
			Authorise(request, token);

			using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

			return response.IsSuccessStatusCode;
		}
		catch (Exception e) when (IsTransport(e))
		{
			// Sign-out is one of the few places where failure is
			// survivable by ignoring it: the app clears its own token
			// regardless, so the member is signed out here even if the
			// server never heard. The stale device row is then revoked
			// from the admin Devices screen.
			return false;
		}
	}

	private async Task<JsonElement?> GetAsync(Uri uri, string? token, CancellationToken cancellationToken)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			Authorise(request, token);

			using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			return await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception e) when (IsTransport(e))
		{
			return null;
		}
	}

	private async Task<HttpResponseMessage?> PostAsync(
		string route,
		string? token,
		object? body,
		CancellationToken cancellationToken)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.Route(route));
			Authorise(request, token);

			if (body is not null)
			{
				request.Content = JsonContent.Create(body, options: JsonOptions);
			}

			// Not disposed here: the caller reads the body and disposes it.
			return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception e) when (IsTransport(e))
		{
			return null;
		}
	}

	private static void Authorise(HttpRequestMessage request, string? token)
	{
		if (!string.IsNullOrEmpty(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}
	}

	private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(body))
			{
				return null;
			}

			using var document = JsonDocument.Parse(body);

			// Cloned because the document is disposed on the way out of
			// this method and a JsonElement does not outlive its document.
			return document.RootElement.Clone();
		}
		catch (Exception e) when (e is JsonException || IsTransport(e))
		{
			return null;
		}
	}

	/// <summary>
	/// The exceptions that mean "the request did not arrive", as opposed
	/// to "the server said no".
	///
	/// <para><see cref="TaskCanceledException"/> is in here because
	/// <see cref="HttpClient"/> raises it for a timeout as well as for a
	/// real cancellation, and a caller that has actually cancelled is not
	/// going to look at the result anyway.</para>
	/// </summary>
	private static bool IsTransport(Exception e) =>
		e is HttpRequestException
			or TaskCanceledException
			or OperationCanceledException
			or WebException
			or IOException;

	private static string Text(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return string.Empty;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? string.Empty,
			JsonValueKind.Number => value.ToString(),
			_ => string.Empty,
		};
	}

	private static long Number(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return 0;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
		{
			return number;
		}

		if (value.ValueKind == JsonValueKind.String
			&& long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		{
			return parsed;
		}

		return 0;
	}

	private static JsonElement? Child(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
			? value
			: null;
}
