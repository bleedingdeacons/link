Feature: Getting a phone onto the fellowship's list
  As a member
  I want to be told what went wrong when I cannot sign in
  So that I know whether to try again or to ask somebody

  A member signs in with the email address their intergroup already
  holds. Two flows meet at one exchange: Google server-side through a
  browser leg that carries a one-time code, Apple client-side with no
  browser leg at all, and a password route for whoever has neither.

  What is settled here is what each outcome *means*. Raising the
  platform sheet, claiming the URI scheme and writing the private half
  into a keystore are all MAUI, and are on-device criteria rather than
  scenarios — see OnDevice.feature.

  Rule: The two flows are told apart by what the start answers

    Scenario: A server-side provider answers a URL to open
      Given a sign-in that answered an authorization URL
      Then the browser is used

    Scenario: A client-side provider answers a nonce instead
      Given a sign-in that answered a nonce and no URL
      Then the browser is not used
      # Fellowship rejects an Apple token minted without the nonce it
      # issued, every single time — which would look like a server bug
      # from the app and an app bug from the server.

  Rule: Changing your mind is not a failure

    An empty error has always meant this — the sign-in screen shows
    nothing rather than accusing somebody of a fault they did not
    commit — but it meant it by convention, discoverable only from a
    comment in one view model. Apple's sheet made cancelling common
    enough to be worth naming: it is the ordinary way to back out of a
    system dialog rather than the rare way to abandon a browser tab.

    Scenario: A cancelled sign-in has nothing to say
      Given the member cancelled the sign-in
      Then the sign-in did not succeed
      And there is nothing to tell them

    Scenario: A refused sign-in carries the server's own words
      Given Fellowship refused the sign-in, saying "That address does not match a member record"
      Then the sign-in did not succeed
      And they are told "That address does not match a member record"
      # The single most common thing to go wrong here and the only one
      # the member can act on. It must reach the screen rather than
      # being flattened into "sign-in failed".

    Scenario: A sign-in that worked carries the session and no complaint
      Given Fellowship accepted the sign-in
      Then the sign-in succeeded
      And there is nothing to tell them

  Rule: A password link says nothing about who is a member

    The endpoint answers the same way for every syntactically valid
    address, because saying otherwise would reveal which of them belong
    to members. The app must not try to be more helpful than the server
    is being.

    Scenario: Asking for a link succeeds whoever the address belongs to
      Given a password link was asked for
      Then it was accepted

    Scenario: Only an unreachable server answers otherwise
      Given a password link was asked for and Fellowship could not be reached
      Then it was not accepted

  Rule: Setting a password distinguishes a spent code from a weak secret

    "That link has expired" and "please use at least 14 characters" call
    for completely different responses from whoever is reading them.

    Scenario Outline: The server's reason reaches the member
      Given setting a password was refused, saying "<reason>"
      Then setting the password did not succeed
      And they are told "<reason>"

      Examples:
        | reason                            |
        | That link has expired             |
        | Please use at least 14 characters |

    Scenario: A password that was accepted says nothing
      Given setting a password was accepted
      Then setting the password succeeded
