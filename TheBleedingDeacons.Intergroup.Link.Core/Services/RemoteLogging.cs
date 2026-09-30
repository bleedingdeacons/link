using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// Where the Better Stack settings come from: Fellowship, not the build.
///
/// <para><b>Why not the build.</b> A token in <c>appsettings.json</c> is
/// in every copy of the APK and IPA, readable by anybody who unzips one,
/// and replacing it takes a release. Fellowship hands it only to a
/// handset that has signed in, refuses a revoked one, and can change or
/// withdraw it from its settings screen.</para>
///
/// <para><b>Three states, not two.</b> The logger controller is told
/// one of these:</para>
/// <list type="bullet">
/// <item><b>Null: not told yet.</b> Before the first sign-in, and
///       after a sign-out. Logs are held in a small on-disk buffer, so
///       what went wrong while signing in is still shipped once
///       Fellowship answers.</item>
/// <item><b>Not valid: told not to ship.</b> The intergroup has no
///       token set. Whatever was held is dropped.</item>
/// <item><b>Valid: ship.</b></item>
/// </list>
///
/// <para>The answer is stored (see <see cref="ILoggingSettingsStore"/>)
/// so the next process starts shipping before it has reached the
/// network. It is forgotten when the session ends, whether the member
/// signed out or the server refused the handset. A revoked handset
/// keeps nothing it was only given because it was signed in.</para>
///
/// <para>The storing, comparing and rebuilding are Inventory's
/// <see cref="ShippingSettings"/>, shared with Register. What is Link's
/// is where the answer comes from — Fellowship, and only for a handset
/// that is signed in — and when it is forgotten.</para>
/// </summary>
public sealed class RemoteLogging
{
	private readonly IFellowshipClient _client;
	private readonly ISessionStore _sessions;
	private readonly ShippingSettings _settings;

	public RemoteLogging(
		IFellowshipClient client,
		ISessionStore sessions,
		ILoggingSettingsStore store,
		ILogShipper shipper)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
		_settings = new ShippingSettings(
			store ?? throw new ArgumentNullException(nameof(store)),
			shipper ?? throw new ArgumentNullException(nameof(shipper)));

		// The sync loop finds out about a refusal, and it has no reason to
		// know logging exists. This hears about it the same way the message
		// list does.
		WeakReferenceMessenger.Default.Register<RemoteLogging, AuthenticationLost>(
			this,
			static (recipient, lost) => _ = recipient.ForgetQuietlyAsync());
	}

	/// <summary>
	/// Build the logger from what was last stored. Runs once at process
	/// start, before the network has been tried.
	/// </summary>
	public Task ApplyStoredAsync() => _settings.ApplyStoredAsync();

	/// <summary>
	/// Ask Fellowship, and rebuild the logger if the answer changed.
	///
	/// <para>Called at launch and whenever the sign-in state changes. With
	/// no session it forgets. With no answer (offline, a server having a
	/// moment, a Fellowship older than the route) it keeps what it has:
	/// losing signal is not a reason to stop shipping, or to drop what is
	/// being held.</para>
	/// </summary>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		var session = await _sessions.LoadAsync().ConfigureAwait(false);
		if (session is null || !session.IsSignedIn)
		{
			await ForgetAsync().ConfigureAwait(false);
			return;
		}

		var fetched = await _client.FetchLoggingAsync(session.Token, cancellationToken).ConfigureAwait(false);
		if (fetched is null)
		{
			Log.Debug("The intergroup did not say where to ship logs; keeping what this handset has");
			return;
		}

		await _settings.ApplyAsync(fetched).ConfigureAwait(false);
	}

	/// <summary>
	/// Drop the stored settings and go back to holding.
	///
	/// <para>A no-op when nothing is stored, so a signed-out launch does
	/// not rebuild a logger that is already holding.</para>
	/// </summary>
	public Task ForgetAsync() => _settings.ForgetAsync();

	private async Task ForgetQuietlyAsync()
	{
		try
		{
			await ForgetAsync().ConfigureAwait(false);
		}
#pragma warning disable CA1031 // A messenger callback has nobody to throw to.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Log settings could not be forgotten after the handset lost its authorisation");
		}
	}
}
