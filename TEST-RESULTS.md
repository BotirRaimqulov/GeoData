# Security test results

```
Passed!  -  Failed: 0,  Passed: 95,  Skipped: 0,  Total: 95
Duration: 19 m 35 s
Target:   net9.0  ·  GeoDataPro.Tests.dll
```

66 test methods across 5 classes; 95 cases once `[Theory]` rows are expanded.
Run on Linux with the .NET 9 SDK. Argon2id runs at reduced cost in tests
(8 MiB, t=2, p=1) so the suite completes in a usable time; production uses
64 MiB, t=3, p=4.

Parallelism is disabled (`AssemblyInfo.cs`). `SecurityHost` and
`DatabaseLocation` hold process-wide state, so concurrent classes corrupted each
other's fixtures. Each test builds its own host over a temporary directory with
an in-memory key store, converts and encrypts a real SQLCipher database, and
deletes the directory afterwards.

| Class | Cases | Result |
|---|---|---|
| `AuthenticationTests` | 19 | pass |
| `AuthorizationTests` | 8 | pass |
| `InjectionAndInputTests` | 33 | pass |
| `StorageAndRecoveryTests` | 12 | pass |
| `AuditAndLeakageTests` | 23 | pass |

## Required scenarios

| # | Scenario | Covered by |
|---|---|---|
| 1 | Wrong password | `WrongPasswordIsRejected`, `UnknownUserIsRejectedWithoutDisclosure` |
| 2 | Brute-force login | `BruteForceIsThrottledThenLockedOut`, `AccountLocksOutAfterRepeatedFailures` |
| 3 | SQL injection | `HostileIdentifiersAreRejected`, `HostileColumnDeclarationsAreRejected`, `IdentifierMustBeOnTheWhitelist`, `OrderDirectionCannotBeInjected`, `InjectionPayloadsInDataAreStoredAsLiteralText` |
| 4 | Unauthorized access | `ViewerCannotWriteEvenWhenUiWouldAllowIt`, `RoleMatrixGrantsExactlyWhatItDeclares` |
| 5 | Role escalation | `OperatorCannotEscalateOwnRole`, `LastAdminCannotBeDemotedOrDisabled`, `UndefinedRoleIsRejectedByDatabaseAndByPolicy` |
| 6 | Unauthorized delete | `PermanentDeleteRequiresDedicatedPermission`, `WriteAcrossWellBoundaryIsRefused` |
| 7 | Path traversal | `PathTraversalIsBlocked`, `AbsolutePathOutsideRootIsBlocked`, `MaliciousFileNamesAreSanitized`, `ArchiveWithTraversalEntryIsRejected` |
| 8 | Invalid / forged token | `ForgedPrincipalCannotBeUsedWithoutStore`, `MalformedHashDoesNotThrow` |
| 9 | Expired session | `ExpiredSessionIsRejected`, `LogoutInvalidatesSession` |
| 10 | Tampered database | `TamperedRowIsDetectedOnNextWrite`, `TamperedWellIsDetectedOnUpdate`, `AuditChainDetectsDeletionAndEdits`, `AuditChainDetectsRemovedEntry` |
| 11 | Corrupted backup | `TamperedBackupIsDetectedAndNotRestored`, `TruncatedAndForeignBackupsAreRejected` |
| 12 | Invalid import file | `NonWorkbookFilesAreRejected`, `ArchiveWithTooManyEntriesIsRejected` |
| 13 | Oversized input | `OversizedAndOutOfRangeValuesAreRejected`, `OverlongTextIsTruncatedNotStoredWhole` |
| 14 | Malformed input | `MalformedCodeListIsSanitized`, `ControlCharactersAreNeutralized` |
| 15 | Sensitive information leakage | `DatabaseFileIsEncryptedAtRest`, `StolenDatabaseFileCannotBeOpenedWithoutTheKey`, `ErrorPresenterGivesReferenceInsteadOfInternals` |
| 16 | Password not stored in plaintext | `PasswordIsNeverStoredInPlaintextOrReversibly`, `IdenticalPasswordsProduceDifferentHashes` |
| 17 | Secrets not logged | `SecretPatternsAreRedacted`, `FileSystemPathsAreRedacted`, `DiagnosticLogNeverWritesSecretsOrStackTraceAtProductionLevel`, `AuditNeverStoresCredentials`, `SecretsNeverAppearInSourceOrConfiguration`, `NoAppSettingsOrEnvFilesAreCommitted` |
| 18 | Unauthorized export | `ExportAndImportAreGated`, `BackupAndRestoreAreGated` |

## What the suite found

### A real defect

`LegacyPlaintextDatabaseIsConvertedWithoutDataLoss` failed. The cause was not
the test.

`DatabaseProtection.Protect` opened the source database with
`SqliteOpenMode.ReadWrite`. An attached database inherits the main connection's
open flags, so `ATTACH DATABASE ... KEY ...` could not create the staging file
and SQLite returned code 14, `SQLITE_CANTOPEN`. The exception was caught,
reported as `ProtectionOutcome.Failed`, logged, and startup continued.

Consequence: a **new** installation was encrypted, because `EnsureCreated`
creates the database through an already-keyed connection. An installation
**upgraded from an existing plaintext database** was not — and nothing told
anyone. The control that finding B2 depends on failed silently.

Fixed by opening the source with `ReadWriteCreate`; the file already exists, so
nothing extra is created. `PrepareStorage` now returns whether the database is
actually encrypted, and startup warns the user plainly when it is not.

### Four wrong test expectations

In each case the security control behaved correctly and the assertion was wrong.
Recorded because "the test was wrong" is a claim that deserves evidence.

* `UndefinedRoleCannotSignIn` tried to plant an invalid role with raw SQL. The
  `CK_Users_Role` check constraint refused the write, so the test could not
  create its own premise. Rewritten to assert that the database rejects it, that
  an unknown role grants no permission, and that `UserAdminService` refuses to
  assign one.
* `WriteAcrossWellBoundaryIsRefused` expected `SecurityDeniedException`. The
  service rejects a row belonging to another well with `E_NOTFOUND`, which
  discloses less about whether that id exists. Rewritten to assert the refusal
  and that neither well was modified.
* `AuditChainDetectsDeletionAndEdits` overwrote an entry's `Action` with
  `LOGIN_SUCCESS` — which was already its value at that position, so nothing was
  tampered with and verification correctly passed. Now forces a different value
  and asserts the write landed before verifying.
* `SecretsNeverAppearInSourceOrConfiguration` flagged `SecurityLog.cs` and
  `AuditService.cs`, which contain words like `api_key` as the redaction
  vocabulary. Those two files are excluded and the scan is line-based.

### An earlier round

The first full run had 27 failures, nearly all from xUnit running test classes
in parallel against process-wide `SecurityHost` / `DatabaseLocation` state.
Disabling parallelism reduced this to 5. Two portability defects surfaced with
it and were fixed in the production code, not worked around in the tests:
`SafeFile` treated `..` traversal and `\` separators as platform-dependent, and
`Redactor` matched its marker list after regex substitution had already removed
the markers.

## Not covered

These are unverified, not verified-and-passing:

* **The WPF application has never been launched.** No UI behaviour is tested.
* **DPAPI sealing and Windows file ACLs never execute here.** Tests use the
  in-memory key store. `WindowsSecureStorage` and `FileAcl` compile only.
* Concurrent multi-process access to one database.
* Argon2id timing at production parameters on field hardware.
* Restore under a torn write or a full disk.
* Long-run behaviour of backup rotation and log rotation.

A green suite says the tested attacks were blocked under the tested conditions.
It does not say the application is secure. `SECURITY.md` section G states what
remains exposed — above all that an attacker with local administrator or root
access is not mitigated.
