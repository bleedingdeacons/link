namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>
/// Announces that a message arrived by push and is now in the history.
///
/// <para>Sent through <c>WeakReferenceMessenger</c> so the message list can
/// redraw without the push handler knowing a view model exists. Without
/// it the list only reloads on <c>OnAppearing</c> or a pull, which leaves
/// the screen stale in exactly the case that matters most: a message
/// arriving while somebody is looking at the list. That was the observed
/// bug — the notification appeared, the message was stored, and the list
/// went on saying "No messages yet" until the page was navigated away
/// from and back.</para>
///
/// <para><b>Push only.</b> A sync is started by the UI and reloads when it
/// finishes, so announcing that too would just make the list redraw twice
/// for one action.</para>
///
/// <para>The message travels with the announcement rather than being
/// re-read, but a subscriber is still expected to reload from the history:
/// the store is the authority on what is held and in what order, and a
/// subscriber that appended this record alone would drift from it the
/// first time two arrived at once.</para>
/// </summary>
public sealed record MessageReceived(LinkMessage Message);
