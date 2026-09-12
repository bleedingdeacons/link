Feature: What counts as read, and whose business it is
  As a member with a phone and a tablet
  I want a message I have read to stay read
  So that an inbox is not a list of things I have already dealt with

  Read state is the member's rather than the handset's — Fellowship keeps
  one row per member — so a message read on a phone is already read when
  the same member's tablet first fetches it. What each handset decides
  for itself is only when to redraw.

  Background:
    Given this handset is signed in to Fellowship

  Rule: Locally first, and the server afterwards

    The server is the authority on read state across a member's devices.
    It is not the authority on whether this one should stop showing a
    message in bold.

    Scenario: Reading a message marks it here and tells the server
      Given message 12 is held
      When message 12 is read
      Then message 12 is read here
      And Fellowship was told message 12 was read

    Scenario: Reading a message with no server to tell still marks it here
      Given message 12 is held
      And this handset is not signed in
      When message 12 is read
      Then message 12 is read here

    Scenario: Reading something this handset does not hold changes nothing
      When message 12 is read
      Then nothing is held

  Rule: A message read elsewhere arrives read

    The read stamp travels in the sealed payload like everything else, so
    a member who read something on their phone does not meet it again as
    unread on their tablet.

    Scenario: A message already read is held as read
      Given message 12 is waiting on the server, already read
      When the handset syncs
      Then message 12 is read here

    Scenario: A message nobody has read is held as unread
      Given message 12 is waiting on the server, still unread
      When the handset syncs
      Then message 12 is not read here

  Rule: What is already held is never fetched again

    Fellowship's query is message_id > since, strictly exclusive, and a
    sync asks from the highest id it holds. So a message arrives once and
    only once, whichever route brought it.

    Scenario: A pushed message is not collected a second time
      When message 12 arrives by push
      And message 12 is waiting on the server, already read
      And the handset syncs
      Then 0 messages were received
      And 1 message is held

    # Which is the one thing to know before reading JsonMessageHistory's
    # SaveAsync: it takes care to keep a local read flag when a second,
    # unread copy of the same message is saved over it, and reasons that
    # the poll's copy is where the read flag comes from. No route reaches
    # that branch — the poll cannot return a message this handset already
    # holds. It is defensive code with a rationale that does not hold, not
    # behaviour this suite can pin down, so nothing here pretends to.
