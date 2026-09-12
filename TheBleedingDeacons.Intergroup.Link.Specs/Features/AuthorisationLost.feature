Feature: A phone the intergroup no longer knows
  As a member
  I want to be told when my phone has been signed out
  So that I am not waiting for messages that are never coming

  A handset can stop being authorised without doing anything wrong: an
  administrator revokes it, a member's record changes in Unity, or
  somebody rotates the site's WordPress salts and every token in the
  fellowship stops working at once. None of that is visible from here
  except as a refused request.

  Until this existed every refusal read as "offline", so a phone that had
  been deliberately cut off looked exactly like one in a tunnel, went on
  polling, and told its member nothing.

  Background:
    Given this handset is signed in to Fellowship

  Rule: Only a refusal signs a handset out

    A network failure, a server error, a gateway having a moment — all
    ordinary, and the next sync tries again. A handset that signed itself
    out over any of them would leave the fellowship every time somebody
    drove through a tunnel, which is worse than the fault this fixes.

    Scenario: A revoked token signs the handset out
      Given Fellowship no longer accepts this handset's token
      When the handset syncs
      Then the handset has signed itself out
      And it holds no token
      And it holds no keypair

    Scenario: A member Fellowship no longer recognises is told which it is
      Given Fellowship no longer has a member record for this address
      When the handset syncs
      Then the handset has signed itself out
      And the member is told something that mentions "no longer has a member record"

    Scenario: A revoked token says something a member can act on
      Given Fellowship no longer accepts this handset's token
      When the handset syncs
      Then the member is told something that mentions "Sign in again"

    Scenario: A handset out of signal stays signed in
      Given Fellowship cannot be reached
      When the handset syncs
      Then the handset is still signed in

    Scenario: A server having a bad day does not sign anybody out
      Given Fellowship answers with a server error
      When the handset syncs
      Then the handset is still signed in

    Scenario Outline: The sync says which it was
      Given Fellowship <state>
      When the handset syncs
      Then the sync failed
      And the failure reads as <failure>

      Examples:
        | state                                            | failure         |
        | cannot be reached                                | network         |
        | no longer accepts this handset's token           | unauthenticated |
        | no longer has a member record for this address   | not eligible    |
        | answers with a server error                      | server          |

  Rule: The messages stay on the phone

    Losing authorisation is far more often an administrator's change or a
    corrected email address than a phone in the wrong hands. Taking a
    member's correspondence away over a clerical fix would be the wrong
    default, so the credentials go and the history does not.

    Scenario: A signed-out handset keeps what it already had
      Given messages 12 and 13 are held
      And Fellowship no longer accepts this handset's token
      When the handset syncs
      Then the handset has signed itself out
      And 2 messages are held

  Rule: The next member does not inherit them

    Which is what makes keeping them defensible. The store records whose
    messages it holds, and enrolment is where that is checked — because
    enrolment is the only moment the handset knows who is now holding it.

    Scenario: The same member signing back in keeps their messages
      Given member 7 is signed in on this phone
      And messages 12 and 13 are held
      And Fellowship no longer accepts this handset's token
      When the handset syncs
      And member 7 signs in on this phone
      Then 2 messages are held

    Scenario: A different member gets an empty inbox
      Given member 7 is signed in on this phone
      And messages 12 and 13 are held
      And Fellowship no longer accepts this handset's token
      When the handset syncs
      And member 9 signs in on this phone
      Then nothing is held

    Scenario: A different member starts from the beginning
      Given member 7 is signed in on this phone
      And messages 12 and 13 are held
      When member 9 signs in on this phone
      And the handset syncs
      Then the server was asked for everything above 0

    Scenario: A store from before owners were recorded is emptied
      Given messages 12 and 13 are held
      When member 7 signs in on this phone
      Then nothing is held
      # Every handset upgrading from an older build, exactly once. The
      # alternative is a guess about whose messages they are.
