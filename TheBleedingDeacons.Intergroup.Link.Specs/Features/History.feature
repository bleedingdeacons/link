Feature: This phone's own copy of the messages
  As a member
  I want my messages on my phone and nowhere I did not put them
  So that I can read them on a train and delete them when I am done

  Fellowship deletes messages once the retention window passes, and a
  member is entitled to keep their own copy for longer — or to keep
  none. The server holds what it needs for audit; the handset holds what
  its owner wants.

  Background:
    Given this handset is signed in to Fellowship

  Scenario: The newest message is the one at the top
    Given messages 12 and 13 and 14 are waiting on the server
    When the handset syncs
    Then the messages read 14, 13, 12

  Scenario: What is held survives the app being killed
    A duty phone is killed rather than closed — swiped away, or taken
    for memory.

    Given messages 12 and 13 are waiting on the server
    When the handset syncs
    And the app is killed and opened again
    Then 2 messages are held

  Rule: Clearing is honest about what it does

    It deletes this handset's copies. It does not unsend anything, other
    people still have theirs, and the intergroup's own record is
    untouched. The confirmation dialog says so, because "clear history"
    reads to most people as "delete the messages".

    Scenario: Clearing empties the phone
      Given messages 12 and 13 are held
      When the history is cleared
      Then nothing is held

    Scenario: Clearing leaves nothing on disk to recover
      Given messages 12 and 13 are held
      When the history is cleared
      And the app is killed and opened again
      Then nothing is held

  Rule: What was cleared stays cleared

    A poll asks for everything above the highest id held, so a store that
    merely deleted its file went back to asking from zero and the server
    refilled it within seconds — in front of a member who had just been
    told it was cleared. What stays behind is a single number inside the
    same encrypted envelope: no message, no subject, no sender.

    Scenario: The next sync does not fetch back what was cleared
      Given messages 12 and 13 are held
      When the history is cleared
      And the handset syncs
      Then the server was asked for everything above 13
      And nothing is held

    Scenario: A message received after a clear moves the mark on
      Given messages 12 and 13 are held
      When the history is cleared
      And message 14 arrives by push
      And the handset syncs
      Then the server was asked for everything above 14

    Scenario: Clearing an empty history does not walk the mark backwards
      Given messages 12 and 13 are held
      When the history is cleared
      And the history is cleared
      And the handset syncs
      Then the server was asked for everything above 13

  Rule: Signing out is not clearing, and the difference matters

    The mark belongs to the member who set it. Left in place for whoever
    signs in next, it would hand them an inbox that silently refuses to
    fetch their own history, with nothing on screen to explain why — so
    signing out resets the store instead of clearing it.

    The steps below say "reset, as signing out resets it" rather than
    "signs out", and the wording is exact rather than coy: sign-out
    itself is DeviceAuthService's, which is MAUI and therefore not
    reachable from here. That it calls this and not the other is an
    on-device criterion — see OnDevice.feature. What is settled here is
    the difference between the two, which is where the bug would be.

    Scenario: The next member starts from the beginning
      Given messages 12 and 13 are held
      When the history is cleared
      And the history is reset, as signing out resets it
      And the handset syncs
      Then the server was asked for everything above 0
      And 2 messages are held

    Scenario: Resetting leaves no messages behind
      Given messages 12 and 13 are held
      When the history is reset, as signing out resets it
      Then nothing is held
      # Signing out on a handed-on phone has to mean the messages are
      # gone from it.
