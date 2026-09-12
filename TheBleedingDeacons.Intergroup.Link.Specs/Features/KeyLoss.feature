Feature: A handset that cannot open its messages
  As an intergroup
  I want a handset whose key has gone to say so
  So that a member is not left with an app that quietly shows them less

  Platforms invalidate keystore entries for reasons that have nothing to
  do with this app — a restored backup, a changed screen lock,
  re-enrolled biometrics. Fellowship cannot see it: from the server a
  handset with a lost private key looks perfectly healthy right up until
  a message it cannot read. So it has to be reported rather than
  inferred.

  Background:
    Given this handset is signed in to Fellowship

  Rule: An envelope that will not open is kept out of the list

    "Will not open" covers every reason at once — a key this handset no
    longer has, a key that does not match the one this was sealed to, a
    payload altered in transit — because the app can do nothing different
    about any of them. What it does about all of them is the same.

    Scenario: A message sealed to another handset is not shown
      Given message 12 was sealed to another handset
      When the handset syncs
      Then nothing is held

    Scenario: A message altered on the way is not shown
      Given message 12 was altered in transit
      When the handset syncs
      Then nothing is held

    Scenario: A handset with no key at all shows nothing
      Given this handset has lost its key
      And message 12 is waiting on the server
      When the handset syncs
      Then nothing is held

    Scenario: A new keypair leaves everything sealed to the old one unreadable
      Given message 12 is waiting on the server
      And this handset generates a new keypair
      When the handset syncs
      Then nothing is held
      # Nobody can undo this. Fellowship never held the private half, so
      # it cannot re-seal them either.

  Rule: The fault is reported once per sync, not once per message

    A handset whose key has gone reports a fault, it does not report
    fifty.

    Scenario: Three unopenable messages are one report
      Given messages 12 and 13 and 14 were sealed to another handset
      When the handset syncs
      Then Fellowship was told once that this handset cannot read its messages

    Scenario: A sync with nothing wrong reports nothing
      Given message 12 is waiting on the server
      When the handset syncs
      Then Fellowship was never told that this handset cannot read its messages

    Scenario: The rest of the page still arrives
      Given message 12 is waiting on the server
      And message 13 was sealed to another handset
      When the handset syncs
      Then message 12 is held
      And 1 message is held
      And Fellowship was told once that this handset cannot read its messages

  Rule: The sync says so, so the screen can

    The service has already told the server. This is what lets the list
    show a banner rather than a short list with no explanation.

    Scenario: A sync that lost something says it lost something
      Given message 12 was sealed to another handset
      When the handset syncs
      Then the sync reports a key fault

    Scenario: A sync that lost nothing does not
      Given message 12 is waiting on the server
      When the handset syncs
      Then the sync reports no key fault

  Rule: A push that will not open raises nothing at all

    Quiet on screen, not quiet in the log — those are different
    decisions. Raising "New message" for something the app cannot show
    would be worse than raising nothing, and the next sync reports the
    fault properly, with a session token to hand.

    Scenario: Nothing is stored and nothing is announced
      Given this handset has lost its key
      When message 12 arrives by push
      Then nothing is held
      And the list was told about nothing
      And nothing was opened

    Scenario: The push reports nothing, and the sync behind it reports properly
      A push handler has no session token. The sync does, which is why
      the report is made from there rather than from here.

      Given this handset has lost its key
      When message 12 arrives by push
      Then Fellowship was never told that this handset cannot read its messages
      When message 12 is waiting on the server
      And the handset syncs
      Then Fellowship was told once that this handset cannot read its messages
