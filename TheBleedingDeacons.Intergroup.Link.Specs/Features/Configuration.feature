Feature: Where this build of Link talks to
  As the person who built this app
  I want its server address checked before it is used
  So that a bearer token and every message body are never sent in the clear

  The session token travels as a bearer credential and message bodies
  are readable on the wire. A plaintext base would put both in the clear
  with nothing in the app saying it had happened. Sign-in is gated on
  this, so an address that is not HTTPS fails closed rather than
  downgrading quietly.

  Scenario Outline: Only an https address is somewhere Link will talk to
    Given a server address of "<address>"
    Then it <verdict> somewhere Link will talk to

    Examples:
      | address                 | verdict |
      | https://aa-bristol.org  | is      |
      | https://aa-bristol.org/ | is      |
      | http://aa-bristol.org   | is not  |
      | aa-bristol.org          | is not  |
      |                         | is not  |

  Rule: The REST namespace is appended rather than configured

    So a site cannot be pointed at a path that is not Fellowship's.

    Scenario: A route is built under Fellowship's own namespace
      Given a server address of "https://aa-bristol.org"
      Then the route for "messages" is "https://aa-bristol.org/wp-json/fellowship/v1/messages"

    Scenario: A trailing slash on the address does not double up
      Given a server address of "https://aa-bristol.org/"
      Then the route for "messages" is "https://aa-bristol.org/wp-json/fellowship/v1/messages"

    Scenario: A leading slash on the route does not either
      Given a server address of "https://aa-bristol.org"
      Then the route for "/messages" is "https://aa-bristol.org/wp-json/fellowship/v1/messages"

  Scenario: A build nobody configured knows where to come back to
    The callback scheme is written in four places and they have to
    agree: here, the Android intent filter, the iOS Info.plist, and
    Fellowship's own allow-list.

    Given a build nobody has configured
    Then the callback is "link://auth"
    And it is not somewhere Link will talk to
