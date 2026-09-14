Feature: Knowing a message got there, and that it was read
  As a member who has written to somebody
  I want to see that my message arrived, and then that it was read
  So that I know whether to wait for an answer or to pick up the phone

  Two members look at the same message from opposite ends. The one who
  sent it wants to know how far it has got; the one it was sent to wants
  to know which messages are new and when each was written. Both answers
  come from the same two facts about one recipient: whether their phone
  has the message, and whether they have read it.

  Background:
    Given this handset is signed in to Fellowship

  Rule: A new message is marked until it is read

    The mark is read state and nothing else. It is not "arrived since the
    app was last opened", because a member with a phone and a tablet would
    then be shown the same message as new twice — see ReadState.feature.

    Scenario: A message nobody has read arrives marked as new
      Given message 12 is waiting on the server, still unread
      When the handset syncs
      Then message 12 is marked as new

    Scenario: Reading it takes the mark away
      Given message 12 is held
      When message 12 is read
      Then message 12 is not marked as new

    Scenario: A message already read on another device arrives unmarked
      Given message 12 is waiting on the server, already read
      When the handset syncs
      Then message 12 is not marked as new

  Rule: The recipient sees when it was sent

    The moment the sender sent it, as Fellowship recorded it — not the
    moment it reached this phone. A message that sat on the server while
    its recipient was in a tunnel was still written at nine o'clock, and
    "when did they send this?" is the question being asked.

    Scenario: A polled message carries the date and time it was sent
      Given message 12 was sent at 2026-09-14 09:30 UTC and is waiting on the server
      When the handset syncs
      Then message 12 reads as sent at 2026-09-14 09:30 UTC

    Scenario: A pushed message carries it too
      When message 12, sent at 2026-09-14 09:30 UTC, arrives by push
      Then message 12 reads as sent at 2026-09-14 09:30 UTC

    Scenario: A message kept on the phone still says when it was written
      Given message 12 was sent at 2026-09-14 09:30 UTC and is waiting on the server
      When the handset syncs
      And the app is killed and opened again
      Then message 12 reads as sent at 2026-09-14 09:30 UTC
      # The history is where the list reads it from. A time that survived
      # the wire and not the file would be right exactly once.

  Rule: The sender sees it arrive, and then sees it read

    Not built. Link has no view of what its member has sent, and
    Fellowship reports no receipts, so these are written ahead of both and
    kept out of the automated run until there is something to bind them
    to.

    "Received" means a recipient's phone has opened the message, not that
    Fellowship stored it — stored is what a successful send already says,
    and a sender told "received" about a message sitting on the server for
    a phone that is switched off has been told something untrue.

    "Read" implies "received". A phone can open a message and have its
    receipt lost on the way back; once the member reads it, the read is
    the proof it arrived.

    @unbuilt @ignore
    Scenario: A message no phone has opened yet shows as sent and nothing more
      Given this member sent message 20 to Jo B
      When the handset syncs
      Then message 20 shows as sent
      And message 20 does not show as received
      And message 20 does not show as read

    @unbuilt @ignore
    Scenario: A message that reached the recipient's phone shows as received
      Given this member sent message 20 to Jo B
      And Jo B has opened message 20
      When the handset syncs
      Then message 20 shows as received
      And message 20 does not show as read

    @unbuilt @ignore
    Scenario: A message the recipient has read shows as read
      Given this member sent message 20 to Jo B
      And Jo B has read message 20
      When the handset syncs
      Then message 20 shows as read

    @unbuilt @ignore
    Scenario: A read whose receipt was lost still counts as received
      Given this member sent message 20 to Jo B
      And Jo B has read message 20, but the receipt for opening it never arrived
      When the handset syncs
      Then message 20 shows as received
      And message 20 shows as read

    @unbuilt @ignore
    Scenario Outline: A message to several people shows what is true of all of them
      A tick that meant "somebody has it" would tell the sender their
      message reached the committee when it had reached one member of it.

      Given this member sent message 20 to Dave B and Jo B
      And Dave B <dave>
      And Jo B <jo>
      When the handset syncs
      Then message 20 shows as <shows>

      Examples:
        | dave               | jo                 | shows    |
        | has not opened it  | has not opened it  | sent     |
        | has opened it      | has not opened it  | sent     |
        | has opened it      | has opened it      | received |
        | has read it        | has opened it      | received |
        | has read it        | has read it        | read     |

  Rule: A recipient's phone says it has the message, whichever route brought it

    Also not built, and the other half of the rule above: a sender can
    only be shown "received" if a recipient's phone says so.

    Both routes have to. A message that arrived by push is never fetched
    again — the poll is strictly exclusive — so the server cannot infer
    receipt from a fetch, and a push handler has no session token to
    report with. The sync behind it does.

    @unbuilt @ignore
    Scenario: A polled message that opens is acknowledged
      Given message 12 is waiting on the server
      When the handset syncs
      Then Fellowship was told message 12 was received

    @unbuilt @ignore
    Scenario: A pushed message is acknowledged by the sync behind it
      When message 12 arrives by push
      And the handset syncs
      Then Fellowship was told message 12 was received

    @unbuilt @ignore
    Scenario: A message that will not open is not acknowledged
      Given message 12 is waiting, sealed to a key this handset has lost
      When the handset syncs
      Then Fellowship was never told message 12 was received
      # A receipt for a message nobody can read would tell the sender it
      # got there, about the one handset where it did not.

  @manual @ignore
  Scenario: The list shows the time as well as the date
    The message page shows "14 September 2026, 09:30" and the list row
    "14 Sep, 09:30". The row showed only the date until 2026-09-14, so two
    messages from the same morning could not be told apart without
    opening them — and the date was UTC, so a message sent just after
    midnight in summer was filed under the day before.

    Given a message sent at 00:30 on 14 September, British Summer Time
    When the message list is shown
    Then its row reads "14 Sep, 00:30"
    And an unread row carries the new-message dot and a bold subject
    And reading it removes both
