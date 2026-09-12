Feature: Addressing a message to somebody
  As a member writing to the fellowship
  I want to pick who it goes to from a list
  So that nobody's address has to be on this phone for me to reach them

  The directory Fellowship sends is anonymous names and opaque member
  ids. No email addresses, no telephone numbers. Link sends ids back and
  the server does the addressing — so a stolen handset yields a list of
  first names rather than the intergroup's contact database, and a
  message cannot be addressed to a non-member by inventing an address.

  Background:
    Given this handset is signed in to Fellowship

  Rule: Recipients are picked, never typed

    Scenario: A message names members by id
      Given the address book holds Dave B and Jo B
      When a message is addressed to Dave B
      And it is sent
      Then the send named 1 member by id
      And the send carried no address of any kind

    Scenario: A committee is addressed by its slug
      Given the address book holds the Literature committee
      When a message is addressed to Literature
      And it is sent
      Then the send named the committee "literature"
      And the send carried no address of any kind

  Rule: One list, one search, one row of chips

    Compose used to hold members and committees apart — a radio switched
    between two lists, and exactly one of either could be chosen, because
    Fellowship refused a message addressed to both. It no longer does,
    and once a message can carry four names and two committees at once,
    "which list is showing?" stops being a question worth asking the
    sender.

    Scenario: Members and a committee can travel together
      Given the address book holds Dave B and Jo B and the Literature committee
      When a message is addressed to Dave B
      And a message is addressed to Jo B
      And a message is addressed to Literature
      And it is sent
      Then the send named 2 members by id
      And the send named the committee "literature"

    Scenario: The same name chosen twice is one recipient
      Given the address book holds Dave B and Jo B
      When a message is addressed to Dave B
      And a message is addressed to Dave B
      And it is sent
      Then the send named 1 member by id

  Rule: A first name identifies nobody in an intergroup with several Daves

    So a row carries a second line — home group, GSR standing, service
    position — and the search reads it too: somebody who wants the
    Secretary, or the GSR from Tuesday Bristol, is describing a person
    the only way they can.

    Scenario Outline: The second line is composed from what is there
      Given a member whose group is "<group>" and who is <gsr> and whose position is "<position>"
      Then their second line reads "<line>"

      Examples:
        | group           | gsr       | position  | line                              |
        | Tuesday Bristol | not a GSR |           | Tuesday Bristol                   |
        | Tuesday Bristol | a GSR     |           | Tuesday Bristol · GSR             |
        | Tuesday Bristol | a GSR     | Secretary | Tuesday Bristol · GSR · Secretary |
        |                 | not a GSR | Secretary | Secretary                         |
        |                 | not a GSR |           |                                   |

    Scenario Outline: Searching reads the name and the second line both
      Given a member Dave B of Tuesday Bristol, who is the Secretary
      When the list is searched for "<term>"
      Then Dave B <appears>

      Examples:
        | term      | appears     |
        | dave      | appears     |
        | tuesday   | appears     |
        | secretary | appears     |
        |           | appears     |
        | treasurer | is not here |

  Scenario: A committee says so on its own row
    "Literature" is a plausible name for a person, and sending the
    fellowship's business to a committee by mistake cannot be taken back.

    Given the address book holds the Literature committee
    Then the Literature row's second line reads "Committee"

  Rule: A reply points at what it answers

    The list is flat. A thread model can be derived from that pointer
    later; a thread id invented now would have to be guessed for every
    existing message.

    Scenario: A reply carries the message it answers
      Given the address book holds Dave B and Jo B
      When a message is addressed to Dave B
      And it is sent in reply to message 12
      Then the send answered message 12

    Scenario: A message that is not a reply answers nothing
      Given the address book holds Dave B and Jo B
      When a message is addressed to Dave B
      And it is sent
      Then the send answered nothing

  Rule: A send that does not happen says why

    Scenario: A refused send carries the server's own reason
      Given the address book holds Dave B and Jo B
      And Fellowship will refuse the send, saying "That committee no longer exists"
      When a message is addressed to Dave B
      And it is sent
      Then the send failed
      And the reason given is "That committee no longer exists"

    Scenario: A handset that is not signed in does not try
      Given this handset is not signed in
      When a message is sent
      Then the send failed
      And the reason given is "This device is not signed in."
      And nothing was sent to the server
