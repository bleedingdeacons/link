Feature: A backup the member keeps themselves
  As a member
  I want a copy of my messages that does not depend on this phone
  So that losing the phone does not have to lose what I was sent

  A backup is one encrypted file, saved through the phone's share sheet and
  chosen again with the file picker. It is a copy of the member's messages
  outside Link. Clearing messages, signing out and Remove this phone do not
  touch it, and the intergroup cannot delete it, so an erasure request cannot
  reach it. Settings says so before one is made.

  The sheet, the picker and the fingerprint asked for first are the phone's,
  and are on-device criteria: see OnDevice.feature.

  Background:
    Given member 7 is signed in on this phone

  Rule: What goes in comes back

    Scenario: A backup restores onto a new phone after the intergroup has swept its copy
      Given messages 12 and 13 are held
      And this member sent message 14 to Jo B
      And a backup is made with the passphrase "a long enough passphrase"
      When the member moves to a new phone
      And Fellowship has swept its messages
      And member 7 signs in on this phone
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup is restored
      And the restore added 3 messages
      And the messages read 13, 12
      And message 14 is kept on this phone, to "Jo B"

    Scenario: Signing out does not touch a backup
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When the history is reset, as signing out resets it
      And member 7 signs in on this phone
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the messages read 13, 12

    Scenario: The style and the switches come back once they are applied
      Given the phone is drawn in SiloTerminal with the sound off
      And a backup is made with the passphrase "a long enough passphrase"
      When the member moves to a new phone
      And member 7 signs in on this phone
      And the phone is changed to LinkBlue with the sound on
      And the backup is restored with the passphrase "a long enough passphrase"
      And the restored choices are applied
      Then the phone is drawn in SiloTerminal with the sound off

  Rule: A backup that cannot be opened changes nothing

    A wrong passphrase and a damaged file cannot be told apart, so they get
    one message.

    Scenario: A wrong passphrase
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When message 13 is deleted
      And the backup is restored with the passphrase "not the same passphrase"
      Then the backup could not be opened
      And restoring says "could not be opened"
      And restoring says "Nothing on this phone has changed"
      And the messages read 12

    Scenario Outline: A file changed after it was made
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When message 13 is deleted
      And one byte of the backup's <part> is changed
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup could not be opened
      And the messages read 12

      Examples:
        | part       |
        | version    |
        | iterations |
        | salt       |
        | nonce      |
        | tag        |
        | contents   |

    Scenario: A file cut short
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When the backup is cut short
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup could not be opened
      And the messages read 13, 12

  Rule: A backup is only ever its own member's

    Nobody's messages land on another member's phone, and a backup gets nobody
    in: it holds no credential, so restoring always needs a sign-in.

    Scenario: Another member's backup is refused
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When member 9 signs in on this phone
      And Fellowship has swept its messages
      And message 20 is waiting on the server
      And the handset syncs
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup belongs to another member
      And restoring says "belongs to another member"
      And the messages read 20

    Scenario: Nobody signed in can restore nothing
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      And this handset is not signed in
      When the backup is restored with the passphrase "a long enough passphrase"
      Then restoring says "Sign in"
      And the messages read 13, 12

    Scenario: No token ever appears in the file
      Given messages 12 and 13 are held
      And this member sent message 14 to Jo B
      When a backup is made with the passphrase "a long enough passphrase"
      Then the backup holds no token and no key

  Rule: It merges, and what the member removed stays removed

    Messages join by id. Anything deleted or cleared on either side stays gone,
    whichever came first, so a restore cannot bring back what the member chose
    to be rid of.

    Scenario: Messages join by id
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When message 14 is waiting on the server
      And the handset syncs
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the restore added 0 messages
      And the messages read 14, 13, 12

    Scenario: A message deleted since the backup stays deleted
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When message 13 is deleted
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup is restored
      And the messages read 12

    Scenario: A message deleted before the backup goes from a new phone too
      Given messages 12 and 13 are held
      When message 13 is deleted
      And a backup is made with the passphrase "a long enough passphrase"
      And the member moves to a new phone
      And member 7 signs in on this phone
      And the handset syncs
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the messages read 12

    Scenario: Messages cleared since the backup stay cleared
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When the history is cleared
      And the backup is restored with the passphrase "a long enough passphrase"
      Then the backup is restored
      And nothing is held

    Scenario: Nor does the next sync fetch them back
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When the history is cleared
      And the backup is restored with the passphrase "a long enough passphrase"
      And the handset syncs
      Then the server was asked for everything above 13
      And nothing is held

    Scenario: A sent copy cleared since the backup stays cleared, even above the messages received
      Given message 10 is held
      And this member sent message 15 to Jo B
      And a backup is made with the passphrase "a long enough passphrase"
      When the history is cleared
      And the backup is restored with the passphrase "a long enough passphrase"
      Then message 15 is not on this phone

    Scenario: Messages cleared before the backup go from a new phone too
      Given messages 12 and 13 are held
      When the history is cleared
      And a backup is made with the passphrase "a long enough passphrase"
      And the member moves to a new phone
      And member 7 signs in on this phone
      And the handset syncs
      And the backup is restored with the passphrase "a long enough passphrase"
      Then nothing is held

    Scenario: What was cleared stays cleared through signing out and back in
      Given messages 12 and 13 are held
      And a backup is made with the passphrase "a long enough passphrase"
      When the history is cleared
      And the history is reset, as signing out resets it
      And member 7 signs in on this phone
      And the backup is restored with the passphrase "a long enough passphrase"
      Then nothing is held

  Rule: The passphrase is the member's alone

    At least 12 characters, typed twice, and never stored. A forgotten
    passphrase means the backup cannot be opened, and nobody can recover it.

    Scenario Outline: What will do for a passphrase
      Then the passphrase "<first>" typed again as "<second>" is <answer>

      Examples:
        | first           | second          | answer   |
        | twelve chars    | twelve chars    | accepted |
        | eleven char     | eleven char     | refused  |
        | twelve chars    | twelve charz    | refused  |

    Scenario: Settings says what a forgotten passphrase costs
      Then the warning about a forgotten passphrase says "nobody can recover it"

    Scenario: Settings says what a backup is
      Then the description of a backup says "outside Link"
      And the description of a backup says "Clearing messages, signing out and Remove this phone"
      And the description of a backup says "an erasure request cannot reach it"
