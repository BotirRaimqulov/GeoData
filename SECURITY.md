# GeoData Pro — Security Audit and Hardening Report

Scope: full audit of the GeoData Pro WPF application and immediate remediation
in code. Target: confidential geological data on a Windows desktop today,
Android clients and a PostgreSQL backend later.

Baseline audited: commit `0b2684e` plus the uploaded `GeoDatalatest.zip`
(three additional reference tables).

---

## 0. Verification environment — read this first

All code in this report was compiled and, where noted, executed. The
verification was done on Linux with the .NET 9 SDK, building the WPF project
with `EnableWindowsTargeting=true`.

What this means:

* The whole solution **compiles**, including the WPF project and XAML.
* The Core security library and its test suite **run**, including real
  SQLCipher encryption, backup sealing, and the audit chain.
* The WPF application itself was **never launched**. No UI behaviour, no
  DPAPI code path, and no Windows ACL code path has been executed. Those are
  Windows-only and must be validated on Windows before release.

Passing tests demonstrate that specific attacks were blocked in specific
conditions. They do not demonstrate that the application is secure. Section G
states what remains exposed.

---

## A. SECURITY SCORE

Scored 0–10 for the state *after* this work. "Before" is the audited baseline.

| Area | Before | After | Note |
|---|---|---|---|
| Architecture | 2 | 8 | UI no longer touches the database; platform-neutral Core extracted |
| Authentication | 0 | 8 | Did not exist; now Argon2id + lockout + session expiry |
| Authorization | 0 | 8 | Did not exist; now RBAC enforced in the service layer |
| Database | 3 | 8 | Constraints, FKs, transactions, concurrency tokens added |
| Encryption | 0 | 7 | Plaintext file; now SQLCipher with OS-protected key |
| Secrets | 4 | 8 | No hardcoded secrets found; key handling now explicit |
| Logging | 2 | 8 | Stack traces to disk; now redacted and rotated |
| File security | 1 | 8 | No validation; now size, signature, archive and path guards |
| Backup | 0 | 8 | Did not exist; now encrypted, verified, rotated |
| Network | n/a | 6 | No network layer yet; abstractions prepared |
| Dependencies | 6 | 7 | No known vulnerable packages; three added, justified below |
| Testing | 0 | 7 | No tests; now 95 security cases, all passing (section F) |

Encryption is 7, not 9, because the key is protected by the logged-in Windows
user, which does not resist an attacker who is already that user or root.

---

## B. CRITICAL FINDINGS

### B1. No authentication — CRITICAL

* **Risk:** Anyone who opens the application reads and edits all confidential
  geological data.
* **Location:** `App.xaml.cs` went straight to `MainWindow`; there was no user
  table anywhere in `Data/`.
* **Why dangerous:** A borrowed or unattended laptop is full disclosure and
  silent modification. Nothing distinguishes one operator from another.
* **Fix:** `AppUser` entity, `AuthenticationService`, `LoginWindow`. First run
  provisions an administrator. Argon2id (64 MiB, t=3, p=4) with a per-password
  random salt. `LoginThrottle` applies exponential backoff after 5 failures;
  `AuthenticationService` locks the account after 8. Sessions expire after
  20 minutes idle and 8 hours absolute.
* **Verification:** `AuthenticationTests` — wrong password, unknown user,
  brute force, lockout and recovery, logout, expiry, disabled account,
  forged principal.

### B2. Database stored in plaintext — CRITICAL

* **Risk:** Copying one file yields every project, well, sample and description.
* **Location:** `AppDbContext.OnConfiguring` used `Data Source={DbPath}` with no
  key; the file sat in `%LOCALAPPDATA%\GeoDataPro\geodata.db`.
* **Why dangerous:** Defeats every in-application control. A USB stick, a
  backup sync client, or a stolen disk is enough. Confidential data was
  readable in any SQLite browser.
* **Fix:** SQLCipher via `SQLitePCLRaw.bundle_e_sqlcipher`. The key is 32 random
  bytes held by `ISecretKeyProvider`, never in source, config, database or logs.
  On Windows the key is sealed with DPAPI (`CurrentUser`) plus per-secret
  entropy. `DatabaseProtection` converts an existing plaintext database in
  place using `sqlcipher_export`, verifies the result with `PRAGMA quick_check`
  before swapping, and keeps the original as a `.plain-<timestamp>` rescue copy
  rather than deleting it.
* **Verification:** `StorageAndRecoveryTests` — file header is not
  `SQLite format 3`, confidential strings absent from the raw bytes, unkeyed
  and wrong-key opens fail, legacy conversion preserves data.

### B2a. Conversion of an existing plaintext database failed silently — HIGH

* **Risk:** The first run would leave the database unencrypted while the
  application carried on as if protection were in place.
* **Location:** `DatabaseProtection.Protect` opened the source database with
  `SqliteOpenMode.ReadWrite`. An attached database inherits the main
  connection's open flags, so `ATTACH DATABASE ... KEY ...` could not create
  the staging file and returned SQLITE_CANTOPEN. The exception was caught and
  reported as `ProtectionOutcome.Failed`, which `SecurityHost.PrepareStorage`
  logged and then continued past.
* **Why dangerous:** Silent failure of the control that B2 depends on. New
  databases were encrypted; upgraded ones were not.
* **Fix:** Open the source with `ReadWriteCreate` so ATTACH can create the
  staging file. The file already exists, so nothing extra is created.
* **Verification:** `StorageAndRecoveryTests.LegacyPlaintextDatabaseIsConvertedWithoutDataLoss`
  — this test found the bug and now passes; it asserts the header changes,
  the data survives, and re-running is a no-op.

### B3. No authorization — CRITICAL

* **Risk:** Any user performs any operation, including permanent deletion and
  export of confidential data.
* **Location:** Every ViewModel called `new AppDbContext()` and wrote directly.
* **Why dangerous:** There was no chokepoint at which a rule could be applied.
* **Fix:** `Permissions` / `AppRole` / `RolePermissions` with five roles
  (Viewer, Operator, Geologist, Auditor, Admin). `AuthorizationService.Require`
  is called at the top of every operation in `GeoDataService`,
  `TransferService`, `BackupService` and `UserAdminService`. The UI disables
  controls as a convenience only; removing that does not grant access.
* **Verification:** `AuthorizationTests` — a Viewer is refused every write path
  even when calling the service directly; an Operator cannot escalate its own
  role; permanent delete requires its own permission.

### B4. Dynamic SQL built by string interpolation — HIGH

* **Risk:** SQL injection if any identifier ever becomes attacker-influenced.
* **Location:** `AppDbContext.ApplyLightMigrations` built
  `pragma_table_info('{table}')`, `ALTER TABLE "{table}" ADD COLUMN "{column}" {decl}`
  and `sqlite_master ... name = '{table}'` by interpolation.
* **Why dangerous:** Not exploitable at the audited commit — every argument was
  a literal — but it is the pattern that becomes a vulnerability the moment a
  column name comes from an import file or a settings screen.
* **Fix:** Values are now parameterised (`SqlQueryRaw(sql, table, column)`).
  Identifiers go through `SqlIdentifier.QuoteFrom` against an explicit table
  whitelist; column types go through `SqlIdentifier.ColumnType`, which accepts
  only five storage classes, `NULL`/`NOT NULL`, and a default of `0`, `1` or
  `''`. Migrations now run inside one transaction.
* **Verification:** `InjectionAndInputTests` — hostile identifiers and column
  declarations rejected; injection payloads stored as literal text with tables
  intact.

### B5. Import silently destroyed data — HIGH

* **Risk:** A malformed or hostile workbook wipes existing wells.
* **Location:** `ExcelService.ImportWorkbook` and the legacy single-well
  importers called `RemoveRange` on all journal, sample and SRP rows of every
  matched well *before* inserting, with no backup and no undo.
* **Why dangerous:** A workbook naming an existing well but containing no
  parseable rows deleted that well's data permanently. No audit record existed.
* **Fix:** Replacement is now soft delete (`Retire`), so rows remain and are
  restorable. `TransferService.ImportAsync` takes a backup first, validates the
  file, and writes an audit record for success and failure alike.
* **Verification:** `StorageAndRecoveryTests.SoftDeleteHidesDataButKeepsItRestorable`;
  gating covered by `AuthorizationTests.ExportAndImportAreGated`.

### B6. No import file validation — HIGH

* **Risk:** Zip bombs, extension spoofing, oversized input, path traversal via
  archive entries.
* **Location:** `ImportWorkbook` did `File.Exists` and handed the path to
  ClosedXML. `.xlsx` is a ZIP; nothing bounded it.
* **Fix:** `SafeFile.ValidateWorkbook` enforces extension, non-zero size, a
  128 MB cap, the `PK\x03\x04` signature, at most 4096 archive entries, 1 GB
  total expanded size, and rejects entries with `..`, absolute paths, drive
  letters or NUL. Row and well counts are capped during parsing, and every
  numeric cell is range-checked.
* **Verification:** `InjectionAndInputTests` — traversal, sanitisation,
  wrong extension, empty file, spoofed signature, traversal entry, entry-count
  bomb.

### B7. Internal details shown to the user and written to disk — HIGH

* **Risk:** Exception text disclosed SQL, file paths and internal structure;
  `errors.log` accumulated full stack traces beside the database, unbounded and
  unencrypted.
* **Location:** `AppNotifier.Error` appended `ex.Message` to a MessageBox;
  `MainViewModel` showed `"Import xatosi: " + ex.Message`; `AppNotifier.LogPath`
  wrote `{ex}` next to `geodata.db`.
* **Fix:** Users now see a plain message plus a reference code
  (`Kod: A1B2C3D4`). `RollingFileLog` scrubs secrets and paths through
  `Redactor`, records exception *types* rather than messages, includes stack
  traces only at Debug level, caps entries at 2000 characters, and rotates at
  2 MB keeping three archives. Release builds log at Warning; Debug at Debug.
* **Verification:** `AuditAndLeakageTests` — secret and path redaction, no
  stack trace at production level, reference-code format, rotation bounds.

### B8. Hard delete with no audit or recovery — HIGH

* **Risk:** Irreversible loss of confidential geological data.
* **Location:** `WellsViewModel.DeleteWell` removed journal, sample and SRP rows
  and the well itself behind a single MessageBox.
* **Fix:** `SoftDeleteWell` marks rows deleted with actor and timestamp;
  `RestoreWell` reverses it. `PurgeWell` requires `data.delete.permanent`
  (Admin only) and first serialises the whole aggregate into `DeletedRecords`
  with an integrity stamp. All three are audited and transactional.
* **Verification:** `AuthorizationTests.PermanentDeleteRequiresDedicatedPermission`,
  `StorageAndRecoveryTests.PurgeArchivesRecordBeforeRemoval`.

### B9. No tamper detection — HIGH

* **Risk:** Direct edits to the database file go unnoticed.
* **Fix:** `HmacIntegrityStamper` writes an HMAC-SHA256 stamp over each well,
  journal, sample and SRP row using a key from the secret provider. The stamp is
  verified before an update; a mismatch raises `IntegrityViolationException`,
  writes an `INTEGRITY_FAILURE` audit record and aborts the write. The audit log
  itself is hash-chained, so deleting or editing an entry breaks the chain.
* **Verification:** `StorageAndRecoveryTests.TamperedRowIsDetectedOnNextWrite`,
  `TamperedWellIsDetectedOnUpdate`, `AuditAndLeakageTests.AuditChainDetects*`.
* **Honest limit:** the stamp key lives under the same protection as the
  database key. It detects a database editor; it does not resist an attacker
  who can read the key.

### B10. Debug symbols committed — MEDIUM

* **Risk:** `GeoDataPro.pdb` (85 KB) in the repository root exposed type names,
  method names and source paths, and worked against the requirement that a
  decompiled binary reveal as little as possible.
* **Fix:** Removed from the repository. `.gitignore` extended with `*.pdb`,
  `*.db`, `*.db-wal`, `*.db-shm`, `*.gdb`, `*.log`, `.env` and secret files.
  Release builds set `DebugType=none`.

### B11. No backup subsystem — MEDIUM

* **Fix:** `BackupService` snapshots through the SQLite backup API (never a raw
  file copy of a live database), verifies the snapshot, then seals it in chunks
  with AES-256-GCM under a key derived by HKDF from a dedicated backup key, with
  a chunk index as associated data and an HMAC-SHA256 over the whole stream.
  Restore verifies before touching the live database and keeps a `.pre-<time>`
  copy. Rotation keeps ten.
* **Verification:** `StorageAndRecoveryTests` — encrypted, verifiable,
  restorable; tampered, truncated and foreign files rejected; rotation bounded.

### B12. Windows-only assumptions in the core — MEDIUM

* **Location:** `AppDbContext.DbPath` hardcoded `SpecialFolder.LocalApplicationData`.
* **Fix:** `IPlatformPaths` and `ISecureStorage` abstract storage and key
  protection. Windows supplies DPAPI plus ACL hardening; Core supplies an
  in-memory implementation and a passphrase-based one for servers and tests.

### B13. Missing schema integrity — MEDIUM

* **Fix:** Explicit foreign keys with `Restrict`, check constraints on depths,
  core recovery and role range, required fields with length limits, indexes on
  the columns actually queried, `PRAGMA foreign_keys/secure_delete/trusted_schema`,
  and a `RowVersion` concurrency token bumped on every save.

---

## C. CHANGED FILES

### New — `src/GeoDataPro.Core` (net9.0, platform-neutral)

| File | Purpose |
|---|---|
| `Security/Argon2PasswordHasher.cs` | Argon2id hashing, encoded-string parsing, rehash detection |
| `Security/PasswordPolicy.cs` | Length, character classes, blocklist, identity check |
| `Security/AuthenticationService.cs` | Login, logout, password change, first-admin provisioning |
| `Security/LoginThrottle.cs` | Per-account exponential backoff |
| `Security/Session.cs` | Principal, clock abstraction, session store with expiry |
| `Security/AuthorizationService.cs` | `Has` / `Demand` / `Require` |
| `Security/Permissions.cs` | Permission constants, roles, role→permission matrix |
| `Security/UserAdminService.cs` | User CRUD, role change, reset, unlock, last-admin guard |
| `Security/DataProtector.cs` | AES-256-GCM seal/open, HKDF derivation |
| `Security/ISecureStorage.cs`, `SecretKeyProvider.cs`, `FileSecureStorageBase.cs`, `InMemorySecureStorage.cs`, `PassphraseSecureStorage.cs` | Key management abstraction and implementations |
| `Security/IntegrityStamp.cs` | HMAC row stamps over a canonical encoding |
| `Security/SqlIdentifier.cs` | Identifier and column-type whitelisting |
| `Security/SecurityExceptions.cs` | Denied, not-authenticated, expired, integrity, secret |
| `Audit/AuditService.cs` | Actions, hash-chained writes, redaction, chain verification |
| `Backup/BackupService.cs` | Snapshot, seal, verify, restore, rotate |
| `Files/SafeFile.cs` | Path containment, filename sanitising, workbook and archive guards, atomic write |
| `Diagnostics/SecurityLog.cs` | Redactor, rolling log, error presenter, reference codes |
| `Data/DatabaseLocation.cs` | Connection string construction, key material |
| `Data/DatabaseProtection.cs` | Plaintext detection and in-place conversion |
| `Data/DbContextFactory.cs` | `IDatabaseProvider` abstraction |
| `Data/SecurityEntities.cs` | `AppUser`, `AuditEntry`, `SecurityFlag`, `DeletedRecord` |
| `Services/GeoDataService.cs` | Authorized, audited, transactional data operations |
| `Services/TransferService.cs` | Guarded import/export |
| `SecurityHost.cs` | Composition root |

### Moved into Core (unchanged behaviour unless noted)

`Data/AppDbContext.cs` (hardened), `Data/Entities.cs` (tracking fields added),
`Data/Seed.cs`, `Services/ExcelService.cs` (soft delete, validation,
cancellation), `Services/RefCache.cs` (goes through the service),
`Services/DescriptionClassifier.cs`.

### New — `src/GeoDataPro.Platform.Windows` (net9.0-windows)

`Security/WindowsSecureStorage.cs` — DPAPI storage, `FileAcl` restricting
directories and files to the current user, `WindowsPlatformPaths`.

### Changed — `src/GeoDataPro.App`

| File | Change |
|---|---|
| `App.xaml.cs` | Builds the host, prepares storage, requires login before the main window, logs out on exit |
| `App.xaml` | `StartupUri` removed so startup is controlled |
| `Services/AppNotifier.cs` | Reference-code errors, exception translation, no raw messages |
| `Services/AppState.cs` | Goes through `IGeoDataService`, exposes the principal and `Can()` |
| `ViewModels/*.cs` | Every `new AppDbContext()` replaced with a service call |
| `Views/LoginWindow.*`, `Views/ChangePasswordWindow.*` | New |
| `MainWindow.xaml` | User label, backup, password and sign-out; sidebar splitter |
| `Views/*.xaml` | Permission-gated controls; grid, editing and panel changes |

### UI changes made in the same pass (user-requested)

| File | Change |
|---|---|
| `Theme/Controls.xaml` | Fixed the `ScrollBar` template whose `Track` was always vertical, so horizontal scrolling never worked anywhere; added splitter, wrapping-header and cell-editor styles |
| `Infrastructure/GridEditing.cs` | Select-all on edit; `EditableNumberConverter` allows a cell to be cleared without a binding error |
| `Views/JournalView.xaml` | Pixel column widths so the grid overflows and scrolls, wrapping headers, three frozen columns, Shift+wheel horizontal scroll |
| `Views/LogChart.cs`, `Views/SrpView.xaml` | New log chart with depth/intensity axes, gridlines, crosshair readout, scale presets, zoom and pan |
| `MainWindow.xaml`, `WellsView.xaml`, `SrpView.xaml`, `JournalView.xaml` | Resizable panels |

### New — `tests/GeoDataPro.Tests`

`AuthenticationTests`, `AuthorizationTests`, `InjectionAndInputTests`,
`StorageAndRecoveryTests`, `AuditAndLeakageTests`, `TestHost`.

### Root

`Directory.Build.props` (Windows targeting for non-Windows builds, Release
symbol suppression), `.gitignore` widened, `GeoDataPro.pdb` deleted,
`GeoDataPro.sln` updated.

---

## D. DATABASE CHANGES

All changes are **additive**. No column or table is dropped, no row is deleted,
and no existing data is rewritten except the one pre-existing backfill.

### New tables

| Table | Purpose |
|---|---|
| `Users` | Accounts, Argon2id hashes, role, lockout state, row version |
| `AuditEntries` | Hash-chained audit trail |
| `SecurityFlags` | Reserved for security settings |
| `DeletedRecords` | Archived aggregates from permanent deletion |

### New columns on `Projects`, `Wells`, `JournalRows`, `SampleRows`, `SrpRows`

`IsDeleted INTEGER NOT NULL DEFAULT 0`, `DeletedUtc TEXT NULL`,
`DeletedByUserId INTEGER NULL`, `RowVersion TEXT NOT NULL DEFAULT ''`
(backfilled with `lower(hex(randomblob(16)))`), `Stamp TEXT NULL`.

### Constraints and indexes (new databases only)

Foreign keys with `Restrict`; check constraints
`CK_JournalRows_Depth`, `CK_JournalRows_Core`, `CK_SampleRows_Depth`,
`CK_Users_Role`; unique index on normalised username; indexes on
`(ProjectId, Number)`, `(WellId, OrderNo)`, `(WellId, SampleNumber)`,
`(WellId, Md)`, audit timestamp and action.

`EnsureCreated` does not alter existing tables, so **an existing database keeps
its old foreign-key and check-constraint definitions**. Application-layer
validation applies in both cases. Migrating an existing database to the full
constraint set requires a rebuild and is deliberately not done automatically.

### File-level change

The database file is converted from plaintext SQLite to SQLCipher on first run.
The original is retained as `geodata.db.plain-<timestamp>`. **This file is still
plaintext and should be moved to secure storage or securely erased once the
conversion is confirmed** — the application does not delete it, because
deleting the only copy of confidential data on the basis of an automated check
is the more dangerous default.

---

## E. SECURITY FEATURES ADDED

Password hashing (Argon2id) · password policy · login rate limiting · account
lockout · session expiry (idle and absolute) · logout · encrypted SQLite
(SQLCipher) · OS-protected key storage (DPAPI) · platform key abstraction ·
AES-256-GCM data protection · RBAC with five roles and seventeen permissions ·
service-layer authorization · hash-chained audit log · audit redaction ·
soft delete with restore · permanent-delete permission · deletion archive ·
row integrity stamps · optimistic concurrency tokens · database constraints and
foreign keys · transactional writes · encrypted and authenticated backups ·
backup verification, rotation and safe restore · workbook validation ·
archive-bomb and traversal guards · export path sanitising · reference-code
error reporting · redacting rotated logs · restrictive file ACLs on Windows.

---

## F. TEST RESULTS

```
Passed: 95   Failed: 0   Skipped: 0   Total: 95   Duration: 19 m 35 s
```

Full detail, including the defect the suite found and the four test
expectations that were wrong, is in `TEST-RESULTS.md`.

The suite covers the eighteen required scenarios: wrong password, brute force,
SQL injection, unauthorized access, role escalation, unauthorized delete, path
traversal, invalid/forged token, expired session, tampered database, corrupted
backup, invalid import file, oversized input, malformed input, sensitive
information leakage, password not stored in plaintext, secrets not logged,
unauthorized export.

Not covered by automated tests, and therefore unverified:

* Everything Windows-specific: DPAPI sealing, file ACLs, the WPF UI.
* Concurrency under real multi-process access.
* Performance of Argon2id at production parameters on target hardware.

---

## G. REMAINING RISKS

**The application is not secure against an attacker with administrative or root
access to the device.** This is a property of local-only architecture, not a
defect that more code in this repository can fix.

### G1. Local administrator or root — NOT MITIGATED

The database key is sealed with DPAPI in the *CurrentUser* scope. Anyone who can
execute code as that user, or as root/SYSTEM, can:

* call `CryptUnprotectData` and recover the database key;
* read the key, and the decrypted data, out of the running process's memory;
* replace the application binary with one that exfiltrates on next launch;
* install a keylogger and capture the login password;
* read the integrity, audit-chain and backup keys, and therefore forge row
  stamps and rewrite the audit chain so tampering leaves no trace.

Encryption at rest defends against a **stolen file** and a **stolen disk**. It
does not defend against a **compromised live machine**. The only real mitigation
is to stop keeping the authoritative copy on the endpoint — see section H.

### G2. Attacker who is the logged-in user

Same as G1 in practice. A user who is authorised to open the application can
extract everything that application can decrypt. Roles limit what the UI and
services will do; they do not limit what a debugger will do.

### G3. Memory exposure

Keys are zeroed after use where the code owns the buffer, but .NET `string`
instances (passwords entered in the UI, connection strings containing the key)
cannot be reliably erased and may be written to the page file or a crash dump.

### G4. Argon2id parameters

64 MiB / t=3 / p=4 follows OWASP guidance but has not been benchmarked on the
target field hardware. If login is too slow, the parameters must be lowered
deliberately and the decision recorded — not lowered silently.

### G5. Constraints on existing databases

Foreign keys and check constraints apply to newly created databases. An upgraded
database relies on application-layer validation only.

### G6. The retained plaintext copy

The `.plain-<timestamp>` rescue file left by conversion is unencrypted. Until an
operator removes it, the original exposure persists.

### G7. Offline brute force of the login password

If the database file is stolen, the Argon2id hashes are exposed to offline
attack. Cost parameters and the password policy raise the bar; they do not
remove it. A weak password remains a weak password.

### G8. Audit log is tamper-*evident*, not tamper-*proof*

Chain verification detects edits and deletions. It cannot prevent them, and an
attacker holding the chain key can rebuild a consistent chain. Off-device
shipping of audit records is the fix, and requires the backend.

### G9. No secure deletion of the underlying file

`PRAGMA secure_delete` overwrites within the database. It does not address
copies left by filesystem journaling, SSD wear levelling, shadow copies, or
backup software.

### G10. Unverified Windows and UI behaviour

Stated in section 0 and repeated here because it matters: DPAPI, ACL and all UI
behaviour compile but have never been executed.

### G11. Supply chain

Three dependencies were added. None had a known advisory at the time of this
work, but that is a point-in-time statement and needs periodic re-checking.

---

## H. FUTURE MIGRATION PLAN

### Current

```
Desktop (WPF)
  → SecurityHost (composition root)
    → IAuthenticationService / IAuthorizationService / IAuditService
      → IGeoDataService  (permission check, audit, transaction)
        → IDatabaseProvider
          → SQLCipher-encrypted SQLite
```

### Target

```
Desktop / Android
  → SecurityHost
    → IAuthenticationService / IAuthorizationService     (token-based)
      → IGeoDataService                                   (unchanged contract)
        → IDatabaseProvider  →  HTTPS API client
                                   → Backend
                                     → authn / authz / audit
                                       → PostgreSQL
```

### What is already in place

| Abstraction | Enables |
|---|---|
| `IGeoDataService` | Business logic is written against a contract, not a `DbContext`; a remote implementation is a drop-in |
| `IDatabaseProvider` | The provider is chosen at composition; SQLite is not referenced by callers |
| `ISecretKeyProvider` / `ISecureStorage` | Android Keystore and a server key manager plug in behind the same interface |
| `IPlatformPaths` | No Windows path is hardcoded in Core |
| `ISessionStore` / `Principal` | Already models issue and expiry times; a JWT maps onto it without changing callers |
| `Permissions` / `RolePermissions` | One matrix shared by client and server, so the server can enforce the same rules |
| `IAuditService` | Same record shape locally and remotely; only the sink changes |
| `IClock` | Deterministic testing of expiry and lockout |
| Async + `CancellationToken` | Already threaded through the service layer for network calls |

### Steps

1. Define the API contract from the existing `IGeoDataService` signatures.
2. Implement the backend, re-using `Permissions`, `RolePermissions` and the
   audit record shape from Core.
3. Move authentication server-side: password verification and lockout become
   backend concerns; the client holds a short-lived token in `ISessionStore`
   and a refresh token in `ISecureStorage`.
4. Add `ApiGeoDataService : IGeoDataService`. ViewModels do not change.
5. Ship audit records to the server, which closes G8.
6. PostgreSQL rules: never exposed to the internet, reachable only from the
   backend, least-privilege application role, parameterised queries only,
   TLS required, migrations under version control, row-level security evaluated
   for per-project isolation.
7. Keep the encrypted local database as an offline cache for field use, with
   a server-side reconciliation on reconnect.

Server-side storage is also the mitigation for G1 and G2: once the endpoint
holds only a cache and a short-lived token, compromising it no longer yields the
full confidential dataset or the ability to forge the audit trail.

---

## I. THREAT MODEL

| # | Threat actor | Attack vector | Current protection | Residual risk | Mitigation |
|---|---|---|---|---|---|
| 1 | Ordinary user | Uses the UI beyond their remit | RBAC enforced in services; audit on every write | Low | Periodic audit review |
| 2 | Temporary physical access | Sits at an unlocked machine | Login required; 20-minute idle expiry | **Medium** — an already-open session is usable | Short OS lock timeout; shorten idle expiry |
| 3 | Database file thief | Copies `geodata.db` to a USB stick | SQLCipher; key not in the file | Low | Rotate the key if theft is suspected |
| 4 | Malicious local process (user-level) | Reads app files | ACLs restrict to the current user; key sealed to that user | **High** — same user means same DPAPI scope | Endpoint protection; move data server-side |
| 5 | Local administrator / root | Full control of the device | None that can hold | **Critical — not mitigated** | Server-side storage; disk encryption; restrict admin rights |
| 6 | SQL injection | Hostile identifiers or values | Parameterised queries; identifier whitelist; EF Core parameterisation | Low | Keep the whitelist current |
| 7 | Network attacker | Intercepts traffic | No network layer today | n/a now | TLS-only backend, certificate pinning on mobile |
| 8 | Compromised client (future) | Stolen device with a valid token | Session expiry; token in secure storage | Medium | Short token lifetime; server-side revocation |
| 9 | Compromised server (future) | Backend breach | n/a today | Deferred | Least privilege, row-level security, off-host audit |
| 10 | Insider with credentials | Legitimate login, illegitimate use | RBAC; audit chain; permanent delete gated; soft delete recoverable | **Medium** — an Admin can do anything, including reshaping the chain | Separate Auditor role; off-device audit shipping; two-person control for permanent delete |
| 11 | Stolen backup | Takes a `.gdb` file | AES-256-GCM plus HMAC; separate key | Low | Store backups on separate media from the key |
| 12 | Malicious import file | Zip bomb, traversal, oversized input | Signature, size, entry and path validation; pre-import backup | Low | Keep caps under review |
| 13 | Data tampering via a SQLite editor | Edits the decrypted database | Row stamps and audit chain detect it | Medium — detection only, and defeated by key access | Server-side authority |

---

## J. DEPENDENCIES

| Package | Version | Why added |
|---|---|---|
| `Konscious.Security.Cryptography.Argon2` | 1.3.1 | .NET has no built-in Argon2id. Widely used, managed implementation. The alternative, PBKDF2 via `Rfc2898DeriveBytes`, is built in but weaker against GPU attack; Argon2id is the OWASP first choice. |
| `SQLitePCLRaw.bundle_e_sqlcipher` | 2.1.10 | Provides the audited SQLCipher native library. Replaces the default `e_sqlite3` bundle; no application code change beyond the connection string. |
| `System.Security.Cryptography.ProtectedData` | 9.0.0 | DPAPI access from .NET on Windows. Microsoft-published, matches the framework version. |

`Microsoft.EntityFrameworkCore.Sqlite` was replaced with
`Microsoft.EntityFrameworkCore.Sqlite.Core` plus `Microsoft.Data.Sqlite.Core` so
the SQLCipher bundle supplies the native provider instead of plain SQLite. This
is the documented way to use SQLCipher with EF Core and is not a downgrade.

No cryptographic primitive is implemented in this repository. AES-GCM,
HMAC-SHA256, HKDF and the RNG all come from `System.Security.Cryptography`;
Argon2id and SQLCipher come from the packages above.

`dotnet list package --vulnerable --include-transitive` reported no advisories
at the time of this work. Versions were pinned rather than moved to latest;
`ClosedXML` (0.104.2), `CommunityToolkit.Mvvm` (8.4.0) and EF Core (9.0.0) were
left at their audited versions because no advisory justified the regression risk.
