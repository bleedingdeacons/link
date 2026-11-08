using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Talks to Fellowship over HTTPS, for what stays in WordPress.
///
/// <para><b>Every method answers a value, never an exception.</b> A phone spends a good
/// deal of its life with no network, and no caller would do anything with an exception
/// except catch it and carry on.</para>
/// </summary>
public sealed class SiteClient : ISiteClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly FellowshipConfiguration _configuration;

	public SiteClient(HttpClient http, FellowshipConfiguration configuration)
	{
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	public async Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal) { ["email"] = email ?? string.Empty };

		using var response = await PostAsync("auth/password/request", body, cancellationToken).ConfigureAwait(false);

		// False means the request never arrived, not that the address was refused:
		// the server answers 200 either way, on purpose.
		return response is not null && response.IsSuccessStatusCode;
	}

	public async Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default)
	{
		var body = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["token"] = code ?? string.Empty,
			["password"] = password ?? string.Empty,
		};

		using var response = await PostAsync("auth/password/complete", body, cancellationToken).ConfigureAwait(false);
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

		// The server's own wording. An expired code and a password its policy refuses
		// need different things from whoever is reading them.
		return PasswordSetResult.Failed(string.IsNullOrEmpty(message)
			? "That did not work. Please ask for a new link and try again."
			: message);
	}

	public async Task<FellowshipDirectory> FetchDirectoryAsync(string idToken, CancellationToken cancellationToken = default)
	{
		var uri = _configuration.Route("directory");

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			if (!string.IsNullOrEmpty(idToken))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
			}

			using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				// Said out loud, because the caller can't: on screen, "the server
				// refused you" and "there is nobody here" look identical.
				Log.Warning("The directory was refused with {Status}", (int)response.StatusCode);

				return FellowshipDirectory.Empty;
			}

			var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);

			return json is null ? FellowshipDirectory.Empty : Parse(json.Value);
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			Log.Debug(e, "The directory request did not arrive");

			return FellowshipDirectory.Empty;
		}
	}

	public Task<Attestation> AttestMemberAsync(string identityToken, CancellationToken cancellationToken = default) =>
		AttestAsync(
			"attest/member",
			identityToken,
			new Dictionary<string, object>(StringComparer.Ordinal),
			"Your membership could not be confirmed. Please try again.",
			cancellationToken);

	public Task<Attestation> AttestAudienceAsync(string deviceToken, SendRequest audience, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(audience);

		// The audience alone, in the shape the Functions are given it. The subject and
		// body are still unsealed here, and the site has no business seeing them.
		var body = new Dictionary<string, object>(StringComparer.Ordinal);

		if (audience.MemberIds.Count > 0)
		{
			body["memberIds"] = audience.MemberIds;
		}

		if (audience.Committees.Count > 0)
		{
			body["committees"] = audience.Committees;
		}

		if (audience.AllGsrs)
		{
			body["gsrs"] = true;
		}

		return AttestAsync("attest/audience", deviceToken, body, "The message could not be sent.", cancellationToken);
	}

	/// <summary>Ask for a statement, and read either it or the site's words.</summary>
	private async Task<Attestation> AttestAsync(
		string route,
		string bearer,
		object body,
		string otherwise,
		CancellationToken cancellationToken)
	{
		using var response = await PostAsync(route, body, cancellationToken, bearer).ConfigureAwait(false);
		if (response is null)
		{
			return Attestation.NotReached("Could not reach the intergroup. Check your connection and try again.");
		}

		var json = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
		var answer = json is { ValueKind: JsonValueKind.Object } read ? read : (JsonElement?)null;

		if (response.IsSuccessStatusCode)
		{
			var token = answer is null ? string.Empty : Text(answer.Value, "attestation");
			if (token.Length > 0)
			{
				return Attestation.Ok(token);
			}

			Log.Warning("POST {Route} answered with no attestation", route);
		}

		// WordPress's own refusal, written to be read by a member: not a member, a
		// committee that can't be sent to, nobody to send to.
		var message = answer is null ? string.Empty : Text(answer.Value, "message");

		return Attestation.Refused(message.Length > 0 ? message : otherwise);
	}

	private static FellowshipDirectory Parse(JsonElement json)
	{
		var members = new List<DirectoryMember>();
		var committees = new List<DirectoryCommittee>();

		if (json.ValueKind != JsonValueKind.Object)
		{
			return FellowshipDirectory.Empty;
		}

		if (json.TryGetProperty("members", out var memberArray) && memberArray.ValueKind == JsonValueKind.Array)
		{
			foreach (var element in memberArray.EnumerateArray())
			{
				var id = Number(element, "id");
				if (id > 0)
				{
					// Read leniently: a directory that failed to parse would take the
					// whole address book with it.
					members.Add(new DirectoryMember
					{
						Id = id,
						Name = Text(element, "name"),
						HomeGroup = Text(element, "group"),
						IsGsr = Flag(element, "gsr"),
						Position = Text(element, "position"),
						// Absent means selectable, not unreachable. See DirectoryMember.HasDevice.
						HasDevice = !element.TryGetProperty("hasDevice", out _) || Flag(element, "hasDevice"),
					});
				}
			}
		}

		if (json.TryGetProperty("committees", out var committeeArray) && committeeArray.ValueKind == JsonValueKind.Array)
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

		return new FellowshipDirectory
		{
			Members = members,
			Committees = committees,
			// Null, absent or nonsense all read as not offered. See FellowshipDirectory.GsrCount.
			GsrCount = (int)Math.Clamp(Number(json, "gsrs"), 0, int.MaxValue),
		};
	}

	private async Task<HttpResponseMessage?> PostAsync(
		string route,
		object body,
		CancellationToken cancellationToken,
		string? bearer = null)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.Route(route))
			{
				Content = JsonContent.Create(body, options: JsonOptions),
			};

			if (!string.IsNullOrEmpty(bearer))
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
			}

			// Not disposed here: the caller reads the body and disposes it.
			var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				// Only the status. These request bodies carry a password or a code.
				Log.Warning("POST {Route} refused with {Status}", route, (int)response.StatusCode);
			}

			return response;
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			Log.Debug(e, "POST {Route} did not arrive", route);

			return null;
		}
	}

	private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			return await FirebaseHttp.ReadAsync(response, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception e) when (FirebaseHttp.IsTransport(e))
		{
			return null;
		}
	}

	private static string Text(JsonElement element, string name) => FirebaseHttp.Text(element, name);

	private static long Number(JsonElement element, string name) => FirebaseHttp.Number(element, name);

	/// <summary>
	/// WordPress will encode a bool as true, as 1 or as "1" depending on how it reached the
	/// response. Anything else is false: claiming a standing somebody doesn't hold is worse
	/// than omitting one they do.
	/// </summary>
	private static bool Flag(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return false;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number => value.TryGetInt64(out var number) && number != 0,
			JsonValueKind.String => value.GetString() is "1" or "true",
			_ => false,
		};
	}
}
