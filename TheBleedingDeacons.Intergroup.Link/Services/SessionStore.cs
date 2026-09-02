using System.Security.Cryptography;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The device token, in <see cref="SecureStorage"/>.
///
/// <para>The whole session goes in as one JSON value rather than the
/// token in secure storage and the member details in
/// <c>Preferences</c>. Splitting them would mean the two could get out of
/// step — a token for one member beside another member's name — and the
/// member details are small enough that there is nothing to gain by
/// keeping them somewhere cheaper.</para>
///
/// <para>The token is issued once and is unrecoverable: Fellowship keeps
/// only an HMAC of it. Losing it means enrolling again, which is why it
/// is here rather than in Preferences.</para>
/// </summary>
public sealed class SessionStore : ISessionStore
{
	private const string Name = "link_device_session";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public async Task<DeviceSession?> LoadAsync()
	{
		string? stored;

		try
		{
			stored = await SecureStorage.GetAsync(Name).ConfigureAwait(false);
		}
		catch (Exception e) when (e is System.Security.SecurityException or CryptographicException or InvalidOperationException)
		{
			// The platform keystore invalidated its own key. Reads as
			// signed out, which is what it amounts to — the member enrols
			// again and gets a working handset, rather than an app that
			// will not open.
			return null;
		}

		if (string.IsNullOrEmpty(stored))
		{
			return null;
		}

		try
		{
			var session = JsonSerializer.Deserialize<DeviceSession>(stored, JsonOptions);

			return session is null || !session.IsSignedIn ? null : session;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public Task SaveAsync(DeviceSession session)
	{
		ArgumentNullException.ThrowIfNull(session);

		return SecureStorage.SetAsync(Name, JsonSerializer.Serialize(session, JsonOptions));
	}

	public Task ClearAsync()
	{
		SecureStorage.Remove(Name);

		return Task.CompletedTask;
	}
}
