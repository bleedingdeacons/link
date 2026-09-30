using System.Security.Cryptography;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The log-shipping settings Fellowship last handed out, in
/// <see cref="SecureStorage"/>.
///
/// <para>Secure storage rather than Preferences because the source token
/// is a credential. It can only write to a log source, but whoever holds
/// it can fill that source with anything.</para>
/// </summary>
public sealed class LoggingSettingsStore : ILoggingSettingsStore
{
	private const string Name = "link_logging";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public async Task<BetterStackConfiguration?> LoadAsync()
	{
		string? stored;

		try
		{
			stored = await SecureStorage.GetAsync(Name).ConfigureAwait(false);
		}
		catch (Exception e) when (e is System.Security.SecurityException or CryptographicException or InvalidOperationException)
		{
			// The keystore invalidated its key, or a push woke the handset
			// before its first unlock. Either way it reads as not told yet,
			// which holds logs rather than losing them.
			return null;
		}

		if (string.IsNullOrEmpty(stored))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<BetterStackConfiguration>(stored, JsonOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public Task SaveAsync(BetterStackConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return SecureStorage.SetAsync(Name, JsonSerializer.Serialize(configuration, JsonOptions));
	}

	public Task ClearAsync()
	{
		SecureStorage.Remove(Name);

		return Task.CompletedTask;
	}
}
