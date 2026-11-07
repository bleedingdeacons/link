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
}
