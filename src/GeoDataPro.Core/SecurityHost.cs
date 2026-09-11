using System;
using System.Security.Cryptography;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Backup;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;

namespace GeoDataPro.Core;

public sealed record StorageReadiness(
    bool DatabaseEncrypted,
    ProtectionOutcome Outcome,
    string? PlaintextRescueFile);

public sealed class SecurityHost : IDisposable
{
    bool _disposed;

    SecurityHost(
        IPlatformPaths paths,
        ISecureStorage storage,
        ISecretKeyProvider keys,
        IDatabaseProvider database,
        ISessionStore sessions,
        IAuthorizationService authorization,
        IAuthenticationService authentication,
        IUserAdminService users,
        IAuditService audit,
        IGeoDataService data,
        ITransferService transfer,
        IBackupService backup,
        IDiagnosticLog log,
        IErrorPresenter errors)
    {
        Paths = paths;
        Storage = storage;
        Keys = keys;
        Database = database;
        Sessions = sessions;
        Authorization = authorization;
        Authentication = authentication;
        Users = users;
        Audit = audit;
        Data = data;
        Transfer = transfer;
        Backup = backup;
        Log = log;
        Errors = errors;
    }

    public IPlatformPaths Paths { get; }
    public ISecureStorage Storage { get; }
    public ISecretKeyProvider Keys { get; }
    public IDatabaseProvider Database { get; }
    public ISessionStore Sessions { get; }
    public IAuthorizationService Authorization { get; }
    public IAuthenticationService Authentication { get; }
    public IUserAdminService Users { get; }
    public IAuditService Audit { get; }
    public IGeoDataService Data { get; }
    public ITransferService Transfer { get; }
    public IBackupService Backup { get; }
    public IDiagnosticLog Log { get; }
    public IErrorPresenter Errors { get; }

    public static SecurityHost? Current { get; private set; }

    public static SecurityHost Require() =>
        Current ?? throw new InvalidOperationException("E_HOST");

    public static SecurityHost Build(
        IPlatformPaths paths,
        ISecureStorage storage,
        string appVersion,
        DiagnosticLevel logLevel,
        IClock? clock = null,
        IPasswordHasher? hasher = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(storage);

        var effectiveClock = clock ?? SystemClock.Instance;
        var keys = new SecretKeyProvider(storage);

        DatabaseLocation.Configure(paths, keys);

        var deviceId = ResolveDeviceId(keys);
        var log = new RollingFileLog(paths.LogDirectory, logLevel);
        var errors = new ErrorPresenter(log);

        var database = new SqliteDatabaseProvider();
        var sessions = new SessionStore(effectiveClock);
        var authorization = new AuthorizationService(sessions);
        var identity = new AppIdentity(appVersion, deviceId);
        var audit = new AuditService(database, sessions, identity, keys, effectiveClock);
        var passwordHasher = hasher ?? new Argon2PasswordHasher();
        var policy = new PasswordPolicy();
        var throttle = new LoginThrottle(effectiveClock);
        var authentication = new AuthenticationService(database, passwordHasher, policy, throttle, sessions, audit, effectiveClock);
        var users = new UserAdminService(database, authorization, passwordHasher, policy, audit, effectiveClock);
        var stamper = new HmacIntegrityStamper(keys);
        var data = new GeoDataService(database, authorization, audit, stamper, effectiveClock);
        var backup = new BackupService(paths, authorization, audit, keys, effectiveClock);
        var transfer = new TransferService(database, authorization, audit, backup);

        var host = new SecurityHost(
            paths, storage, keys, database, sessions, authorization, authentication,
            users, audit, data, transfer, backup, log, errors);

        Current = host;
        return host;
    }

    public StorageReadiness PrepareStorage()
    {
        var keyMaterial = DatabaseLocation.CurrentKeyMaterial();
        var target = DatabaseLocation.DatabaseFile;
        var outcome = ProtectionOutcome.NothingToDo;
        string? rescue = null;

        try
        {
            DatabaseProtection.Adopt(DatabaseLocation.LegacyDatabaseFile, target, keyMaterial);
            outcome = DatabaseProtection.Protect(target, keyMaterial, out rescue);
            if (outcome == ProtectionOutcome.Failed)
                Log.Write(DiagnosticLevel.Error, "storage-protect");
        }
        catch (Exception ex)
        {
            outcome = ProtectionOutcome.Failed;
            Log.Write(DiagnosticLevel.Error, "storage-protect", ex);
        }

        Paths.Harden(Paths.DataDirectory);
        Paths.Harden(Paths.KeyDirectory);
        Paths.Harden(Paths.BackupDirectory);
        Paths.Harden(Paths.LogDirectory);

        Database.EnsureReady();
        Paths.Harden(target);

        var state = DatabaseProtection.Inspect(target);
        var encrypted = state != ProtectionState.Unprotected;

        if (!encrypted)
            Log.Write(DiagnosticLevel.Error, "storage-unprotected");

        return new StorageReadiness(encrypted, outcome, rescue);
    }

    static string ResolveDeviceId(ISecretKeyProvider keys)
    {
        var raw = keys.GetOrCreate("dev", 8);
        try
        {
            return Convert.ToHexString(raw).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Sessions.Clear();
        if (Storage is IDisposable disposable) disposable.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
