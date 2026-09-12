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

    Scenario: A message sealed to a key this handset has lost is not shown
      Given message 12 is waiting, sealed to a key this handset has lost
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

  Rule: A replaced key costs nothing that is still on the server

    This said the opposite until 2026-09-12, in this file and in five
    other places, and it was wrong everywhere. The reasoning went:
    messages were sealed to a public key whose private half is gone, and
    Fellowship never held that private half, so nobody can re-seal them.

    The second half does not follow. Fellowship is not holding sealed
    messages — it holds the bodies in plain text and seals them afresh on
    every single fetch, to whichever key the asking device presents. So
    replacing a key loses nothing the retention window still covers. The
    next sync brings all of it back.

    What that does cost is real and worth keeping in view: it means the
    handset's keypair protects the wire and the notification tray, and
    protects nothing at all in the database.

    Scenario: Everything comes back once the new key is presented
      Given messages 12 and 13 are waiting, sealed to a key this handset has lost
      When the handset syncs
      Then nothing is held
      When this handset presents its new key to Fellowship
      And the handset syncs
      Then 2 messages are held

    Scenario: Until it is presented, the server goes on sealing to the old one
      Given message 12 is waiting, sealed to a key this handset has lost
      When the handset syncs
      And the handset syncs
      Then nothing is held
      # Fellowship cannot tell. From the server a handset that has lost
      # its private half looks perfectly healthy right up until a message
      # it cannot read, which is why the fault has to be reported rather
      # than inferred.

    Scenario: The push is the delivery that is really lost, and the poll is not
      Given this handset generates a new keypair
      When message 12 arrives by push
      Then nothing was opened
      And nothing is held
      When this handset presents its new key to Fellowship
      And message 12 is waiting on the server
      And the handset syncs
      Then message 12 is held
      # The asymmetry in one scenario. A push was sealed once, at send,
      # and nothing re-sends it. The same message on the next poll is
      # sealed afresh and arrives perfectly.

  Rule: The fault is reported once per sync, not once per message

    A handset whose key has gone reports a fault, it does not report
    fifty.

    Scenario: Three unopenable messages are one report
      Given messages 12 and 13 and 14 are waiting, sealed to a key this handset has lost
      When the handset syncs
      Then Fellowship was told once that this handset cannot read its messages

    Scenario: A sync with nothing wrong reports nothing
      Given message 12 is waiting on the server
      When the handset syncs
      Then Fellowship was never told that this handset cannot read its messages

    Scenario: A tampered message does not take the rest of the page with it
      Given message 12 is waiting on the server
      And message 13 was altered in transit
      When the handset syncs
      Then message 12 is held
      And 1 message is held
      And Fellowship was told once that this handset cannot read its messages

  Rule: The sync says so, so the screen can

    The service has already told the server. This is what lets the list
    show a banner rather than a short list with no explanation.

    Scenario: A sync that lost something says it lost something
      Given message 12 is waiting, sealed to a key this handset has lost
      When the handset syncs
      Then the sync reports a key fault

    Scenario: A sync that lost nothing does not
      Given message 12 is waiting on the server
      When the handset syncs
      Then the sync reports no key fault

  Rule: The recovery is offered only while there is something to recover

    Replacing a key asks a member to sign in again, which is a great deal
    of screen for a fault most of them will never meet — the usual cause
    is a changed screen lock, and it is uncommon. So Settings hides the
    whole card until a sync says otherwise, and shows it again the moment
    one does.

    Scenario: A sync that could not open something offers the recovery
      Given message 12 is waiting, sealed to a key this handset has lost
      When the handset syncs
      Then the recovery is offered

    Scenario: A sync that opened everything takes the offer away
      Given message 12 is waiting on the server
      When the handset syncs
      Then the recovery is not offered

    Scenario: A sync with nothing waiting takes it away too
      When the handset syncs
      Then the recovery is not offered
      # A screen that only ever hears about faults can never stop showing
      # one, so an empty sync has to say so as plainly as a full one.

    Scenario: A sync that never reached the server says nothing either way
      Given Fellowship cannot be reached
      When the handset syncs
      Then nothing has been said about the recovery
      # An unreachable server knows nothing about this handset's key. A
      # phone in a tunnel must not lose an offer it needs.

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
