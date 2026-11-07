using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Turns what a phone keeps into what Firebase wants on each request.
///
/// <para>The phone keeps a refresh token, in the place it used to keep
/// Fellowship's device token. Firebase wants an ID token, which lasts an hour,
/// so one is fetched when needed and held in memory only.</para>
///
/// <para>Nothing read from an ID token here decides what is allowed. The server
/// checks it, and these are only the addresses to write to.</para>
/// </summary>
internal sealed class FirebaseIdentity(HttpClient http, FirebaseConfiguration configuration)
{
	/// <summary>Fetched this long before the old one expires, so a slow request doesn't carry a dead token.</summary>
	private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

	private readonly SemaphoreSlim _gate = new(1, 1);

	private string _refreshToken = string.Empty;
	private FirebaseUser? _user;
	private DateTimeOffset _expires;

	/// <summary>The device behind this refresh token, or why it can't be had.</summary>
	public async Task<(FirebaseUser? User, FellowshipFailure Failure)> CurrentAsync(
		string refreshToken,
		CancellationToken cancellationToken)
	{
		try
		{
			await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				if (_user is not null && _refreshToken == refreshToken && DateTimeOffset.UtcNow < _expires)
				{
					return (_user, FellowshipFailure.None);
				}

				return await RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			Log.Debug(e, "The Firebase token refresh did not arrive");

			return (null, FellowshipFailure.Network);
		}
	}

	/// <summary>Sign in with a custom token, from a Function or from Fellowship.</summary>
	public async Task<SignedIn?> SignInWithCustomTokenAsync(string customToken, CancellationToken cancellationToken)
	{
		var body = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["token"] = customToken,
			["returnSecureToken"] = true,
		};

		return await SignInAsync("signInWithCustomToken", body, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Exchange Apple's ID token for a Firebase one.
	/// </summary>
	/// <param name="appleIdToken">What Apple's sheet returned.</param>
	/// <param name="rawNonce">The value whose SHA-256 the sheet was given.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	public async Task<SignedIn?> SignInWithAppleAsync(string appleIdToken, string rawNonce, CancellationToken cancellationToken)
	{
		var postBody = "id_token=" + Uri.EscapeDataString(appleIdToken)
			+ "&providerId=apple.com"
			+ "&nonce=" + Uri.EscapeDataString(rawNonce);

		var body = new Dictionary<string, object>(StringComparer.Ordinal)
		{
			["postBody"] = postBody,
			["requestUri"] = "https://" + configuration.ProjectId + ".firebaseapp.com",
			["returnSecureToken"] = true,
		};

		return await SignInAsync("signInWithIdp", body, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Hold this device's token, so the next request doesn't fetch it again.</summary>
	public async Task HoldAsync(SignedIn signedIn, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			Hold(signedIn.RefreshToken, signedIn.User, signedIn.ExpiresInSeconds);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Let go of what is held, after signing out.</summary>
	public async Task ForgetAsync()
	{
		await _gate.WaitAsync().ConfigureAwait(false);

		try
		{
			_user = null;
			_refreshToken = string.Empty;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<(FirebaseUser? User, FellowshipFailure Failure)> RefreshAsync(
		string refreshToken,
		CancellationToken cancellationToken)
	{
		using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
		});

		using var request = WithKey(new HttpRequestMessage(HttpMethod.Post, configuration.SecureToken) { Content = form });
		using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
		var json = await FirebaseHttp.ReadAsync(response, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			// Only Google saying the token itself is finished means stop asking.
			// Anything else is an outage, and a phone that signed out over an
			// outage would lose its key for nothing.
			var code = json is null ? string.Empty : FirebaseHttp.ErrorMessage(json.Value);
			var finished = code is "TOKEN_EXPIRED" or "USER_DISABLED" or "USER_NOT_FOUND" or "INVALID_REFRESH_TOKEN";

			Log.Warning("The Firebase token refresh was refused with {Status}: {Code}", (int)response.StatusCode, code);

			return (null, finished ? FellowshipFailure.Unauthenticated : FellowshipFailure.Server);
		}

		var idToken = json is null ? string.Empty : FirebaseHttp.Text(json.Value, "id_token");
		var user = FirebaseUser.From(idToken);

		if (user is null)
		{
			Log.Warning("The Firebase token refresh answered something this app could not read");

			return (null, FellowshipFailure.Server);
		}

		Hold(refreshToken, user, ExpirySeconds(FirebaseHttp.Text(json!.Value, "expires_in")));

		return (user, FellowshipFailure.None);
	}

	/// <summary>
	/// Say which app is using the key. Without this, a key restricted to the app is refused.
	/// See <see cref="FirebaseConfiguration.ClientHeaders"/>.
	/// </summary>
	private HttpRequestMessage WithKey(HttpRequestMessage request)
	{
		foreach (var (name, value) in configuration.ClientHeaders)
		{
			request.Headers.TryAddWithoutValidation(name, value);
		}

		return request;
	}

	private async Task<SignedIn?> SignInAsync(
		string method,
		Dictionary<string, object> body,
		CancellationToken cancellationToken)
	{
		try
		{
			using var request = WithKey(new HttpRequestMessage(HttpMethod.Post, configuration.Identity(method))
			{
				Content = JsonContent.Create(body),
			});
			using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			var json = await FirebaseHttp.ReadAsync(response, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode || json is null)
			{
				Log.Warning(
					"Firebase {Method} was refused with {Status}: {Code}",
					method,
					(int)response.StatusCode,
					json is null ? string.Empty : FirebaseHttp.ErrorMessage(json.Value));

				return null;
			}

			var idToken = FirebaseHttp.Text(json.Value, "idToken");
			var refreshToken = FirebaseHttp.Text(json.Value, "refreshToken");

			if (idToken.Length == 0 || refreshToken.Length == 0)
			{
				return null;
			}

			return new SignedIn(
				idToken,
				refreshToken,
				FirebaseUser.From(idToken),
				ExpirySeconds(FirebaseHttp.Text(json.Value, "expiresIn")));
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			Log.Debug(e, "Firebase {Method} did not arrive", method);

			return null;
		}
	}

	private void Hold(string refreshToken, FirebaseUser? user, int expiresInSeconds)
	{
		_refreshToken = refreshToken;
		_user = user;
		_expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds) - ExpiryMargin;
	}

	private static int ExpirySeconds(string value) =>
		int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
}

/// <summary>A sign-in's tokens. <see cref="User"/> is null for an identity that isn't a device yet.</summary>
internal sealed record SignedIn(string IdToken, string RefreshToken, FirebaseUser? User, int ExpiresInSeconds);

/// <summary>A device as its ID token describes it.</summary>
internal sealed record FirebaseUser(string IdToken, string DeviceId, long MemberId)
{
	/// <summary>Read an ID token, or null when it isn't a device's.</summary>
	public static FirebaseUser? From(string idToken)
	{
		var parts = idToken.Split('.');
		if (parts.Length != 3)
		{
			return null;
		}

		try
		{
			using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
			var root = claims.RootElement;

			var deviceId = FirebaseHttp.Text(root, "sub");
			var memberId = FirebaseHttp.Number(root, "memberId");

			return deviceId.Length == 0 || memberId <= 0 ? null : new FirebaseUser(idToken, deviceId, memberId);
		}
		catch (Exception e) when (e is JsonException or FormatException)
		{
			return null;
		}
	}
}

/// <summary>What the Firebase clients share about reading an answer.</summary>
internal static class FirebaseHttp
{
	public static async Task<JsonElement?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(body))
			{
				return null;
			}

			using var document = JsonDocument.Parse(body);

			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>The exceptions that mean the request didn't arrive.</summary>
	public static bool IsTransport(Exception e) =>
		e is HttpRequestException
			or TaskCanceledException
			or OperationCanceledException
			or IOException;

	/// <summary>Google's error text: <c>{"error":{"message":...}}</c>, or a bare <c>{"error":"..."}</c>.</summary>
	public static string ErrorMessage(JsonElement json)
	{
		if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("error", out var error))
		{
			return string.Empty;
		}

		return error.ValueKind == JsonValueKind.String ? error.GetString() ?? string.Empty : Text(error, "message");
	}

	public static string Text(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
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

	public static long Number(JsonElement element, string name)
	{
		var text = Text(element, name);

		return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
	}

	public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
