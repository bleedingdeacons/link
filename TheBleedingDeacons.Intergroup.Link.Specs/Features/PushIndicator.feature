Feature: Whether this phone can actually be pushed to
  As a member who wonders why messages seem to arrive late
  I want to be told what push is doing on this phone
  So that I find out before somebody asks why I did not reply

  Two separate things have to be true before a message can announce
  itself on a closed app, and they fail for completely different
  reasons: the build has to carry push at all, and the phone's owner has
  to have left notifications switched on for Link. A single yes-or-no
  collapses those into one word and says nothing about which of them to
  go and fix.

  Hand carries the same four states in the same shape. The wording
  differs because the consequence does: a Hand handset that is not
  pushed to is a responder who might miss a call, and a Link phone that
  is not pushed to is a member who reads a message later.

  Scenario Outline: The three inputs collapse in the order they can fail
    Given a phone whose build <transport> push
    And whose owner has <permission> notifications
    And which the intergroup <registration>
    Then push reads as <state>
    And the indicator is <colour>

    Examples:
      | transport | permission | registration              | state        | colour  |
      | carries   | left on    | has a registration for    | active       | #2E7D32 |
      | carries   | left on    | holds no registration for | unregistered | #F9A825 |
      | carries   | turned off | has a registration for    | blocked      | #B3261E |
      | has no    | left on    | has a registration for    | unsupported  | #757575 |

  Scenario: Permission is reported before registration
    A registration on a silenced phone would report success for
    something that shows nothing.

    Given a phone whose build carries push
    And whose owner has turned off notifications
    And which the intergroup holds no registration for
    Then push reads as blocked

  Scenario: Before anything has been read, push reads as unavailable
    A phone briefly under-promising is one somebody checks, and the
    opposite is one that quietly stops telling anybody about their
    messages.

    Given nothing has been read about push yet
    Then push reads as unsupported
    And push is not working

  Scenario Outline: Only the states somebody can act on ask for attention
    Given push reads as <state>
    Then it <attention> attention

    Examples:
      | state        | attention   |
      | active       | needs no    |
      | unregistered | needs       |
      | blocked      | needs       |
      | unsupported  | needs no    |

  Rule: Amber is for a phone the sync is still carrying

    Red is kept for the state where a message can arrive and the member
    is never told.

    Scenario Outline: Every state says something different, in the member's terms
      Given push reads as <state>
      Then its headline is "<headline>"
      And its detail mentions "<detail>"

      Examples:
        | state        | headline                             | detail                    |
        | active       | Push notifications are on            | with Link closed          |
        | unregistered | Push notifications are not connected | none of them is lost      |
        | blocked      | Push notifications are turned off    | this phone's own settings |
        | unsupported  | Push notifications are not available | when you open the app     |
