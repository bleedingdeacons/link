Feature: A message arrives, however it travelled
  As a member of the fellowship
  I want a message to reach me whether or not the push did
  So that "I never got it" is not something the network gets to decide

  Every message is stored by Fellowship before any push is attempted, and
  Link polls as well as listening. A phone in a tunnel catches up when it
  surfaces; a handset whose FCM token silently rotated still gets its
  messages; a build with no google-services.json gets all of them, just
  later.

  Background:
    Given this handset is signed in to Fellowship

  Rule: Push and poll are the same message by different routes

    A pushed envelope and a polled one are the same bytes in the same
    format, so there is one place that opens them and one path into the
    history. A push handler that knew how to decrypt is how the two
    routes drift apart until a message displays differently depending on
    how it arrived.

    Scenario: A pushed message is opened and kept
      When message 12 arrives by push
      Then message 12 is held
      And its subject reads "Intergroup meeting moved"

    Scenario: A polled message is opened and kept
      Given message 12 is waiting on the server
      When the handset syncs
      Then message 12 is held
      And its subject reads "Intergroup meeting moved"

    Scenario: A message that arrived by push is not fetched again
      When message 12 arrives by push
      And message 12 is waiting on the server
      And the handset syncs
      Then 0 messages were received
      And 1 message is held

  Rule: The poll asks for what this handset does not have

    Nothing here assumes a push arrived. A sync asks for everything above
    the highest id held, so a message whose push was dropped, delayed by
    Doze, or sent to a token that has moved on is picked up on the next
    pass regardless.

    Scenario: A handset with nothing asks from the beginning
      When the handset syncs
      Then the server was asked for everything above 0

    Scenario: A handset that has been pushed to asks from there
      When message 12 arrives by push
      And the handset syncs
      Then the server was asked for everything above 12

    Scenario: A dropped push is collected by the next sync
      Given messages 12 and 13 are waiting on the server
      When the handset syncs
      Then 2 messages are held

  Rule: An empty inbox is an answer; an unreachable server is not

    A caller that conflated them would clear its unread badge every time
    the network dropped.

    Scenario: Nothing new is a successful sync
      When the handset syncs
      Then the sync succeeded
      And 0 messages were received

    Scenario: A server that cannot be reached is a failed sync
      Given Fellowship cannot be reached
      When the handset syncs
      Then the sync failed
      And the unread count is not known

    Scenario: A handset that is not signed in does not ask
      Given this handset is not signed in
      When the handset syncs
      Then the sync failed
      And the server was never asked for anything

    Scenario: The unread total comes from the server rather than being counted here
      Given message 12 is waiting on the server
      And the server says 4 are unread
      When the handset syncs
      Then the unread count is 4

  Rule: A message arriving by push redraws whatever is on screen

    Without this the list goes on showing what it loaded when the page
    appeared, which is wrong in exactly the case that matters most — a
    message arriving while somebody is looking at it. That was the
    observed bug: the notification appeared, the message was stored, and
    the list went on saying "No messages yet" until the page was
    navigated away from and back.

    Scenario: A pushed message announces itself
      When message 12 arrives by push
      Then the list was told about message 12

    Scenario: A synced message does not
      Given message 12 is waiting on the server
      When the handset syncs
      Then the list was told about nothing

  Scenario: The id beside a push is not the id of the message
    The id in the clear is what lets a poll page without opening
    anything. A push carries its own id inside the seal, and that is the
    one the message is filed under.

    When an envelope labelled 99 carrying message 12 arrives by push
    Then message 12 is held
    And 1 message is held
