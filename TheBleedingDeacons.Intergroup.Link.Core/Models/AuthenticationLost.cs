namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Announces that this handset is no longer signed in, and why.
///
/// <para>Sent through <c>WeakReferenceMessenger</c> for the same reason
/// <see cref="MessageReceived"/> is: the sync loop runs where there is no
/// view model and no navigation, and it has no business knowing whether
/// either exists. Whoever is on screen listens and acts.</para>
///
/// <para>By the time this is sent the credentials are already gone — the
/// device token and the keypair both. <b>The message history is
/// deliberately still there.</b> Losing authorisation is usually an
/// administrator's change or a lapsed member record rather than a phone
/// in the wrong hands, and taking somebody's correspondence away over
/// what is often a clerical correction would be the wrong default. The
/// handset that signs in next is the place that decision gets revisited:
/// see <c>IMessageHistory.AdoptAsync</c>, which empties the store when
/// the member signing in is not the one it belongs to.</para>
/// </summary>
/// <param name="Failure">Which refusal it was.</param>
/// <param name="Reason">What to tell the member, in words they can act on.</param>
public sealed record AuthenticationLost(FellowshipFailure Failure, string Reason);
