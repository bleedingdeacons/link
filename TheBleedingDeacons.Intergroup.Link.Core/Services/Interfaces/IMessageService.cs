using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// The one place that knows how a message gets from Fellowship into this
/// handset's history.
///
/// <para>Both routes end here. A push wakes the app and hands it one
/// envelope; a poll fetches several. Neither the push handler nor the
/// view models should know how to open an envelope, and this is why they
/// do not.</para>
/// </summary>
public interface IMessageService
{
	/// <summary>
	/// Fetch everything newer than what is held, open it, and store it.
	/// </summary>
	Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Open one pushed envelope and store it.
	///
	/// <para>Answers the message so a push handler can raise a
	/// notification naming it, or null when the envelope could not be
	/// opened — in which case the handler should raise nothing and let
	/// the next sync try again.</para>
	/// </summary>
	Task<LinkMessage?> ReceivePushAsync(string wrappedKey, string sealedPayload, CancellationToken cancellationToken = default);

	/// <summary>Mark read here and, if it can be reached, on the server.</summary>
	Task MarkReadAsync(long messageId, CancellationToken cancellationToken = default);

	Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What a sync did.</summary>
public sealed record SyncResult
{
	/// <summary>Messages opened and stored on this pass.</summary>
	public int Received { get; init; }

	/// <summary>Unread total per the server, or -1 when the fetch failed.</summary>
	public int Unread { get; init; } = -1;

	/// <summary>
	/// True when at least one envelope arrived that this handset could
	/// not open.
	///
	/// <para>Almost always means the keypair has been replaced — a changed
	/// screen lock, a restored backup. The service has already told the
	/// server; this is here so the UI can say something rather than
	/// showing a short list with no explanation.</para>
	/// </summary>
	public bool KeyFault { get; init; }

	/// <summary>False when the sync did not complete.</summary>
	public bool Succeeded { get; init; } = true;

	/// <summary>
	/// Why, when it did not.
	///
	/// <para>A screen that shows "offline" for a revoked handset is worse
	/// than one that shows nothing: it tells the member to check their
	/// signal about a device the intergroup has deliberately cut off.
	/// </para>
	/// </summary>
	public FellowshipFailure Failure { get; init; } = FellowshipFailure.None;

	/// <summary>A sync that was never attempted — no session to attempt it with.</summary>
	public static SyncResult Failed { get; } = new() { Succeeded = false };

	/// <summary>A sync the server refused, or that never arrived.</summary>
	public static SyncResult FailedWith(FellowshipFailure failure) =>
		new() { Succeeded = false, Failure = failure };
}
