using TheBleedingDeacons.Intergroup.Link.Models;

namespace TheBleedingDeacons.Intergroup.Link.Services.Interfaces;

/// <summary>
/// What Link still asks of the intergroup's own site.
///
/// <para>The member list and passwords stay in WordPress as part of Unity, so these do
/// too. Everything else is on Firebase.</para>
/// </summary>
public interface ISiteClient
{
	/// <summary>
	/// Ask for a link to set or reset a password.
	///
	/// <para>True for any address the server accepted the request for, which is every
	/// syntactically valid one. It deliberately doesn't say whether a link was sent,
	/// because that would reveal which addresses belong to members.</para>
	/// </summary>
	Task<bool> RequestPasswordLinkAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>Set a password, using the code from that email.</summary>
	Task<PasswordSetResult> SetPasswordAsync(string code, string password, CancellationToken cancellationToken = default);

	/// <summary>The address book, asked for as this device with a Firebase ID token.</summary>
	Task<FellowshipDirectory> FetchDirectoryAsync(string idToken, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ask the site to vouch that whoever just signed in is a member, for the Functions
	/// that enrol, take over or re-key a device.
	///
	/// <para>The Functions can't ask WordPress themselves: its host turns away calls from
	/// Google's servers. So the phone asks, and carries the site's signed answer to them.</para>
	/// </summary>
	/// <param name="identityToken">The Firebase ID token from the sign-in, never a refresh token.</param>
	Task<Attestation> AttestMemberAsync(string identityToken, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ask the site to vouch for who a message reaches, for the Functions that resolve
	/// an audience and send to it.
	///
	/// <para>Only the audience goes: the subject and body never leave the phone unsealed.</para>
	/// </summary>
	/// <param name="deviceToken">This device's Firebase ID token.</param>
	Task<Attestation> AttestAudienceAsync(string deviceToken, SendRequest audience, CancellationToken cancellationToken = default);
}

/// <summary>
/// The site's signed statement for a Function, or the words to show instead.
///
/// <para>The statement is short-lived and only the Functions can check it, so it is
/// carried as it came and never kept.</para>
/// </summary>
public sealed record Attestation
{
	/// <summary>The signed statement, or empty when there isn't one.</summary>
	public string Token { get; init; } = string.Empty;

	/// <summary>False when the request never reached the site, as opposed to being refused by it.</summary>
	public bool Arrived { get; init; }

	/// <summary>Why not, in the site's own words where it gave any.</summary>
	public string Error { get; init; } = string.Empty;

	public bool Succeeded => Token.Length > 0;

	public static Attestation Ok(string token) => new() { Token = token, Arrived = true };

	public static Attestation Refused(string error) => new() { Arrived = true, Error = error };

	public static Attestation NotReached(string error) => new() { Error = error };
}
