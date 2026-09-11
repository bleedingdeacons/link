@manual @ignore
Feature: What only a phone can answer
  On-device acceptance criteria, verified on a handset or an emulator.
  Every one of these is either a platform API, a Firebase delivery, or a
  signing arrangement, and none of them is reachable from a test host.

  They are written down here rather than left in a README because
  several are the sharpest hazards this app has — one of them destroys
  messages permanently and is reached by a perfectly ordinary gesture.

  Rule: Nothing readable crosses Google

    The tray shows the sender's name and "New message". That is all it
    ever shows, because that is all the app puts there.

    Scenario: The notification names the sender and nothing else
      Given a message with a subject and a body
      When it arrives by push with Link closed
      Then the tray shows the sender's name and "New message"
      And neither the subject nor the body appears anywhere on the lock screen

    Scenario: The payload on the wire is ciphertext
      When Fellowship pushes a message
      Then the FCM data carries only the k and p fields
      And neither names the member, the subject or the body

  Rule: A push wakes a closed app, and a poll covers what it does not

    Scenario: A closed app is woken by a push
      Given Link is not running
      When a message is sent to this member
      Then the phone shows a notification
      And opening it shows the message already decrypted

    Scenario: A build with no Firebase configuration still gets everything
      Given an Android build made without google-services.json
      When a message is sent to this member
      Then nothing appears until the app is opened
      And every message is there when it is
      # The same documented state as a phone in a tunnel, and as the
      # iOS head today. Late, not lost.

    Scenario: The token is re-registered at every launch
      Given a handset that enrolled before Firebase had issued a token
      When the app is started
      Then the current token is sent to Fellowship regardless
      # OnNewToken fires on rotation. A handset whose token simply was
      # not ready at enrolment never heard from it, and stayed poll-only
      # permanently while looking healthy from both ends.

  Rule: The private half never leaves, which is the whole design and the whole hazard

    Fellowship holds only the public half, so it cannot re-seal anything
    to a key that no longer exists. Nobody can.

    Scenario: Uninstalling destroys every message ever sent to this handset
      Given a handset holding messages
      When the app is uninstalled and installed again
      Then it enrols as a new device
      And every message sealed to the old key is unreadable forever
      # This is what `adb uninstall` does, and what deleting and
      # reinstalling a sideloaded iOS build does. Re-signing in place
      # keeps the key; removing the app does not.

    Scenario: A changed screen lock can invalidate the keystore entry
      Given a handset holding messages
      When the screen lock is changed and the keystore entry is invalidated
      Then the message list shows a key-fault banner
      And Settings offers a new keypair
      And the device row survives, so nobody re-enrols

    Scenario: Signing out clears the history as well as the token
      Given a handset holding messages
      When the member signs out
      Then no message is left on the phone
      And the next member to sign in fetches their own history from the beginning
      # Handing a phone on has to mean the messages are gone from it.
      # History.feature settles what reset does; this is the criterion
      # that sign-out is what calls it.

  Rule: Notifications are the platform's to refuse

    Scenario: A first run asks for what it needs
      Given the app is opened for the first time on Android 13 or later
      When it needs to post notifications
      Then it asks for POST_NOTIFICATIONS
      And a member who says no still has a working app

    Scenario: A phone whose notifications were switched off says so on Settings
      Given notifications are turned off for Link in the phone's own settings
      When Settings is opened
      Then push reads as turned off, in red
      And the words name the phone's own settings as the place to fix it

    Scenario: The arrival sound can be turned off
      Given a member who takes this phone into meetings
      When they turn the arrival sound off
      Then a message landing with the app open makes no noise
      And it still arrives
      # An app that cannot be quietened is an app that gets uninstalled.

  Rule: iOS is designed for and not delivered to

    Scenario: An iOS build collects its messages by polling
      Given an iOS build signed by a free personal team
      When a message is sent to this member
      Then no push arrives
      And the message is there on the next sync
      # aps-environment is grantable only to a paid Developer Program
      # team, so the entitlement is compiled out and FirebasePush finds
      # no GoogleService-Info.plist.

    Scenario: Sign in with Apple is hidden rather than offered and broken
      Given a build made without -p:LinkPaidTeam=true
      When the sign-in screen is shown
      Then there is no Continue with Apple button
      And Google sign-in works through the browser

    Scenario: A sideloaded build stops working after seven days
      Given an .ipa re-signed with a free Apple ID
      When seven days pass
      Then the app refuses to launch until it is re-signed
      And re-signing in place keeps the handset's key and its messages
      # Deleting it to reinstall does not — see the uninstall scenario
      # above. Anything past a demonstration wants the paid account.
