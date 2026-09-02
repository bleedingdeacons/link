namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// What this handset holds after enrolling: the bearer token, and who it
/// says the member is.
///
/// <para>The token is issued once and is unrecoverable afterwards — the
/// server keeps only an HMAC of it. Losing it means enrolling again,
/// which is why it lives in SecureStorage rather than Preferences.</para>
///
/// <para>The member id and name are a convenience for the UI, not an
/// authority. The server re-resolves the member on every single request
/// and will refuse this token the moment they stop being one, so nothing
/// here should ever be used to decide whether something is
/// allowed.</para>
/// </summary>
public sealed record DeviceSession
{
	public required string Token { get; init; }

	public long DeviceId { get; init; }

	public long MemberId { get; init; }

	public string MemberName { get; init; } = string.Empty;

	public bool IsSignedIn => !string.IsNullOrEmpty(Token);
}
