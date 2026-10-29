namespace TheBleedingDeacons.Intergroup.Link.Support;

/// <summary>
/// This handset's key for <see cref="EmailRedactor"/>'s tokens, made the
/// first time it is asked for and kept in <c>SecureStorage</c>.
///
/// <para>It never leaves the handset: that is what keeps a token from being
/// matched to an address by anyone holding only the logs. SecureStorage
/// rather than Preferences, which is where Register keeps its key, because
/// on iOS Preferences go into device backups and Link keeps everything of
/// this kind on the handset that wrote it — see
/// <c>SecureStorage.DefaultAccessible</c> in <c>MauiProgram</c>.</para>
///
/// <para>Losing it costs nothing but continuity: a new key, new tokens. So a
/// key that cannot be read or stored is replaced, or used for this process
/// only, and never stops a launch.</para>
///
/// <para>Read before the logger exists, so it cannot log. What went wrong is
/// kept in <see cref="Fault"/> for <c>MauiProgram</c> to report once it
/// can.</para>
/// </summary>
public static class LogRedactionKey
{
	private const string StorageKey = "link_log_redaction_key";

	private static readonly Lock Sync = new();
	private static byte[]? _key;

	/// <summary>Why the stored key could not be used, if it could not.</summary>
	public static Exception? Fault { get; private set; }

	public static byte[] Get()
	{
		lock (Sync)
		{
			return _key ??= LoadOrCreate();
		}
	}

	private static byte[] LoadOrCreate()
	{
		// Blocking, as LinkServices does for the history key and for the
		// same reason: it runs once per process, and the push service's
		// process builds the same logger from a synchronous callback.
		try
		{
			var stored = SecureStorage.GetAsync(StorageKey).GetAwaiter().GetResult();
			if (!string.IsNullOrEmpty(stored))
			{
				var key = Convert.FromBase64String(stored);
				if (key.Length >= 16)
				{
					return key;
				}
			}
		}
#pragma warning disable CA1031 // Deliberately broad: any failure means "make a new one".
		catch (Exception e)
#pragma warning restore CA1031
		{
			Fault = e;
		}

		var fresh = EmailRedactor.NewKey();
		try
		{
			SecureStorage.SetAsync(StorageKey, Convert.ToBase64String(fresh)).GetAwaiter().GetResult();
		}
#pragma warning disable CA1031 // Deliberately broad, as above.
		catch (Exception e)
#pragma warning restore CA1031
		{
			// Still redacts with it. Tokens just will not carry over to the
			// next start, which is a loss of continuity, not of privacy.
			Fault ??= e;
		}

		return fresh;
	}
}
