Feature: Messages kept together as conversations
  As a member writing back and forth with the fellowship
  I want a message and everything said about it kept in one place
  So that I can follow an exchange without piecing it together from two lists

  The list used to be two lists — what arrived, and what was sent —
  behind a switch. An answer and the message it answered were then on
  different screens, and following an exchange meant flicking between
  them and matching subjects by eye.

  So the top level is now one list of conversations. A conversation is
  a message that answers nothing, and every message since that answers
  it, either way round: what this member received and what they sent sit
  together.

  Conversations are derived from reply_to, and from nothing else. Every
  message already carries the id it answers, so there is no thread id to
  invent — one invented now would have to be guessed for every message
  already held. Nothing about a conversation is stored; it is worked out
  from the history each time the list is drawn.

  Messages are numbered here as Fellowship numbers them, rising with
  time, so a higher number is always the later message.

  Background:
    Given this handset is signed in to Fellowship

  Rule: A message that answers nothing starts a conversation

    Scenario: A message received that is not a reply is its own conversation
      Given message 12 is held
      Then the conversations read 12
      And conversation 12 has no replies

    Scenario: A message sent that is not a reply is its own conversation
      Given this member sent message 20 to Jo B
      Then the conversations read 20
      And conversation 20 has no replies

    Scenario: Two unrelated messages are two conversations
      Given messages 12 and 13 are held
      Then the conversations read 13, 12

  Rule: Everything after the first message sits under it, in the order it was sent

    One level deep. A reply to a reply sits beside its parent under the
    first message, not beneath it: a phone screen read with a thumb has
    no room for an indented tree, and an exchange between two people is
    a sequence far more often than it is a branch.

    Scenario: An answer to something this member sent sits under it
      Given this member sent message 20 to Jo B
      When message 21, answering 20, arrives by push
      Then the conversations read 20
      And conversation 20 holds replies 21

    Scenario: This member's answer sits under what it answers
      Given message 12 is held
      When this member replies to message 12 with message 20
      Then the conversations read 12
      And conversation 12 holds replies 20

    Scenario: A reply to a reply sits under the first message, not under its parent
      Given this member sent message 20 to Jo B
      And message 21, answering 20, arrives by push
      When this member replies to message 21 with message 22
      And message 23, answering 22, arrives by push
      Then the conversations read 20
      And conversation 20 holds replies 21, 22, 23

    Scenario: Replies that came by the poll are filed the same way
      Given this member sent message 20 to Jo B
      And message 21, answering 20, is waiting on the server
      When the handset syncs
      Then conversation 20 holds replies 21

  Rule: The newest activity is at the top

    A conversation somebody has just answered is the one its member is
    most likely to want, however long ago it began.

    Scenario: An answer brings an older conversation back to the top
      Given messages 12 and 13 are held
      When message 14, answering 12, arrives by push
      Then the conversations read 12, 13

  Rule: A forward starts a new trail

    A forward is sent answering nothing, on purpose. The people it goes
    to were not part of the exchange it came from, and folding their
    answers back into it would put words in front of the original
    correspondents that were never addressed to them.

    It is an ordinary new message whose body quotes the original.
    Fellowship has no forward route and needs none. The recipients start
    empty, because who it goes to is the whole decision being made.

    Scenario: A forwarded message answers nothing
      Given message 12, with the subject "Literature order", is held
      And the address book holds Dave B and Jo B
      When message 12 is forwarded to Dave B as message 30
      Then the send answered nothing
      And the conversations read 30, 12
      And conversation 12 has no replies

    Scenario: A forward carries the original
      Given message 12, with the subject "Literature order", is held
      When message 12 is prepared for forwarding
      Then the forward's subject reads "Fwd: Literature order"
      And the forward's body quotes message 12

    Scenario: Forwarding a forward does not stack the prefix
      Given message 12, with the subject "Fwd: Literature order", is held
      When message 12 is prepared for forwarding
      Then the forward's subject reads "Fwd: Literature order"

  Rule: A reply whose original is not on this phone stands alone

    The original may have been cleared, swept by the retention window,
    or sent from another handset — which keeps its own copy of what it
    sent, not this one's. A reply to something the member cannot see is
    still a message they need to see, so it is shown as a conversation
    of its own rather than hidden under nothing.

    Nothing about that is stored, so if the original does turn up — a
    push delayed behind the answer to it — the reply joins it.

    Scenario: A reply to a message this phone does not hold starts its own conversation
      When message 14, answering 9, arrives by push
      Then the conversations read 14
      And conversation 14 has no replies

    Scenario: The reply joins the original when it turns up
      Given message 14, answering 12, arrives by push
      When message 12 arrives by push
      Then the conversations read 12
      And conversation 12 holds replies 14

    Scenario: After a clear, an answer to what was cleared stands alone
      Given message 12 is held
      When the history is cleared
      And message 13, answering 12, arrives by push
      Then the conversations read 13

  Rule: The message view says when it has been answered

    So a member opening something does not have to scroll the
    conversation to learn whether they already wrote back. Only answers
    sent from this phone count, because only those are kept here.

    Scenario: A message this member answered says so
      Given message 12 is held
      When this member replies to message 12 with message 20
      Then message 12 shows it was answered with message 20

    Scenario: A message nobody here answered says nothing
      Given message 12 is held
      Then message 12 shows no answer

    Scenario: The newest answer is the one shown
      Given message 12 is held
      When this member replies to message 12 with message 20
      And this member replies to message 12 with message 21
      Then message 12 shows it was answered with message 21

    Scenario: A message this member sent and then followed up says so too
      Given this member sent message 20 to Jo B
      When this member replies to message 20 with message 21
      Then message 20 shows it was answered with message 21

    Scenario: Somebody else's answer is not this member's
      Given this member sent message 20 to Jo B
      When message 21, answering 20, arrives by push
      Then message 20 shows no answer
