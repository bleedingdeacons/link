Feature: Deleting one message from this phone
  As a member
  I want to delete a message I no longer want on my phone
  So that I can tidy up without clearing everything

  Each message on the conversation screen has a Delete button under it.
  It is Clear messages for one message: it deletes this handset's copy,
  and nothing else. It does not unsend anything, other people still
  have theirs, and the intergroup's own record is untouched. The
  confirmation says so, because "delete" reads to most people as "take
  it back".

  Background:
    Given this handset is signed in to Fellowship

  Rule: It takes this phone's copy, received or sent

    Scenario: Deleting a received message
      Given messages 12 and 13 are held
      When message 12 is deleted
      Then the messages read 13

    Scenario: Deleting the only message leaves nothing
      Given message 12 is held
      When message 12 is deleted
      Then nothing is held

    Scenario: Deleting a sent message
      Given this member sent message 20 to Jo B
      When message 20 is deleted
      Then message 20 is not on this phone

    Scenario: What was deleted is still gone after the app is killed
      Given messages 12 and 13 are held
      When message 12 is deleted
      And the app is killed and opened again
      Then the messages read 13

  Rule: What was deleted stays deleted

    A message deleted before any poll collected it — one that came by
    push — is still above where the poll starts, so the next sync fetches
    it again; what is left of it here is what keeps it out. Deleting does
    not move where the poll starts: it used to, and that skipped any
    earlier message whose push had dropped.
    And a message to a committee this member sits on arrives back as
    well as being sent, under the same id, so a copy can turn up after
    the member has deleted theirs.

    Scenario: The next sync does not fetch back the newest message deleted
      Given messages 12 and 13 are held
      When message 13 is deleted
      And the handset syncs
      Then the server was asked for everything above 13
      And the messages read 12

    Scenario: A second copy arriving later is not taken back in
      Given this member sent message 20 to Jo B
      When message 20 is deleted
      And message 20 arrives by push
      Then message 20 is not on this phone

  Rule: Deleting a message does not cut its conversation in two

    Conversations are walked through reply_to, so a gap in the middle
    would leave everything after it standing alone. Only the deleted
    message's two ids are kept — no subject, no body, no sender — and
    the walk goes through them.

    Scenario: Deleting from the middle keeps the rest together
      Given this member sent message 20 to Jo B
      And message 21, answering 20, arrives by push
      When this member replies to message 21 with message 22
      And message 21 is deleted
      Then the conversations read 20
      And conversation 20 holds replies 22

    Scenario: Deleting the first message leaves the next at the head
      Given this member sent message 20 to Jo B
      And message 21, answering 20, arrives by push
      And message 22, answering 21, arrives by push
      When message 20 is deleted
      Then the conversations read 21
      And conversation 21 holds replies 22

    Scenario: An answer to a deleted message still joins its conversation
      Given this member sent message 20 to Jo B
      And message 21, answering 20, arrives by push
      When message 21 is deleted
      And message 22, answering 21, arrives by push
      Then the conversations read 20
      And conversation 20 holds replies 22

    Scenario: The conversation stays together after the app is killed
      Given this member sent message 20 to Jo B
      And message 21, answering 20, arrives by push
      When this member replies to message 21 with message 22
      And message 21 is deleted
      And the app is killed and opened again
      Then conversation 20 holds replies 22
