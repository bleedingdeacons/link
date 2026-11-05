# backup-v1.linkbackup

A backup made once by `BackupFile` at format version 1 and committed, so that
every later version has to go on reading it. **Do not regenerate it.** The point
of it is that its bytes are old. A format change raises the version and leaves
this file alone, and `MessageBackupTests.A_v1_backup_still_restores` is what
stops a change from quietly dropping version 1.

It holds test messages for member 7 and no key: no device block, no token and no
private key, which `The_v1_fixture_holds_no_key_and_no_token` checks. The
passphrase is in the test because it protects nothing.
