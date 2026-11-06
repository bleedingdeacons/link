using TheBleedingDeacons.Intergroup.Link.Models;
using TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Link.Services;

/// <summary>
/// The client the app holds: whichever back end <see cref="BackendSwitch"/> says.
///
/// <para>Chosen on every call rather than once, so a phone that moves doesn't need
/// restarting, and nothing above this seam has to know there are two.</para>
///
/// <para>The Firebase client is built on first use, because on iOS the configuration
/// its API key comes from isn't loaded until after the app has started building.</para>
/// </summary>
public sealed class SwitchingFellowshipClient : IFellowshipClient
{
	private readonly BackendSwitch _switch;
	private readonly IFellowshipClient _fellowship;
	private readonly Lazy<IFellowshipClient> _firebase;

	public SwitchingFellowshipClient(BackendSwitch backend, IFellowshipClient fellowship, Func<IFellowshipClient> firebase)
	{
		_switch = backend ?? throw new ArgumentNullException(nameof(backend));
		_fellowship = fellowship ?? throw new ArgumentNullException(nameof(fellowship));
		ArgumentNullException.ThrowIfNull(firebase);
		_firebase = new Lazy<IFellowshipClient>(firebase);
	}

	private IFellowshipClient Active => _switch.Current == LinkBackend.Firebase ? _firebase.Value : _fellowship;

	public Task<SignInStart?> StartSignInAsync(string provider, CancellationToken cancellationToken = default) =>
		Active.StartSignInAsync(provider, cancellationToken);

	public Task<EnrolmentResult> EnrolAsync(EnrolmentRequest request, CancellationToken cancellationToken = default) =>
		Active.EnrolAsync(request, cancellationToken);

	public Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default) =>
		Active.RequestPasswordLinkAsync(email, cancellationToken);

	public Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default) =>
		Active.SetPasswordAsync(code, password, cancellationToken);

	public Task<InboxPage> FetchInboxAsync(string token, long sinceId, CancellationToken cancellationToken = default) =>
		Active.FetchInboxAsync(token, sinceId, cancellationToken);

	public Task<bool> MarkReadAsync(string token, long messageId, CancellationToken cancellationToken = default) =>
		Active.MarkReadAsync(token, messageId, cancellationToken);

	public Task<bool> MarkReceivedAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
		Active.MarkReceivedAsync(token, messageIds, cancellationToken);

	public Task<IReadOnlyList<MessageReceipt>?> FetchReceiptsAsync(string token, IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default) =>
		Active.FetchReceiptsAsync(token, messageIds, cancellationToken);

	public Task<SendResult> SendAsync(string token, SendRequest request, CancellationToken cancellationToken = default) =>
		Active.SendAsync(token, request, cancellationToken);

	public Task<FellowshipDirectory> FetchDirectoryAsync(string token, CancellationToken cancellationToken = default) =>
		Active.FetchDirectoryAsync(token, cancellationToken);

	public Task<bool> UpdatePushTokenAsync(string token, string pushToken, CancellationToken cancellationToken = default) =>
		Active.UpdatePushTokenAsync(token, pushToken, cancellationToken);

	public Task<RotateKeyResult> RotateKeyAsync(string token, RotateKeyRequest request, CancellationToken cancellationToken = default) =>
		Active.RotateKeyAsync(token, request, cancellationToken);

	public Task<bool> ReportKeyFaultAsync(string token, CancellationToken cancellationToken = default) =>
		Active.ReportKeyFaultAsync(token, cancellationToken);

	public Task<bool> SignOutAsync(string token, CancellationToken cancellationToken = default) =>
		Active.SignOutAsync(token, cancellationToken);
}
