namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Announces whether this handset can currently open its messages.
///
/// <para>Sent after every sync that actually reached the server, for the
/// same reason <see cref="MessageReceived"/> and
/// <see cref="AuthenticationLost"/> are: the sync loop runs where there
/// is no view model, and more than one screen needs to know.</para>
///
/// <para><b>What it is for is hiding a recovery nobody needs.</b> The
/// "fix messages that will not open" flow asks a member to sign in
/// again, which is a lot of screen for something most members will never
/// meet — the usual cause is a changed screen lock, and it is uncommon.
/// So Settings shows it only while there is a fault to fix, and this is
/// how Settings finds out.</para>
///
/// <para>Carries false as well as true. A fault that has been fixed, or
/// was a single mangled payload rather than a lost key, has to be able to
/// take the offer away again.</para>
/// </summary>
/// <param name="Faulted">Whether the last sync failed to open something.</param>
public sealed record KeyFaultChanged(bool Faulted);
