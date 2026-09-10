using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.Core.Audit;

public static class AuditActions
{
    public const string LoginSuccess = "LOGIN_SUCCESS";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string LoginBlocked = "LOGIN_BLOCKED";
    public const string Logout = "LOGOUT";
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string DeletePermanent = "DELETE_PERMANENT";
    public const string Restore = "RESTORE";
    public const string Import = "IMPORT";
    public const string Export = "EXPORT";
    public const string Backup = "BACKUP";
    public const string BackupRestore = "RESTORE_BACKUP";
    public const string UserCreated = "USER_CREATED";
    public const string UserUpdated = "USER_UPDATED";
    public const string RoleChanged = "ROLE_CHANGED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string SettingsChanged = "SETTINGS_CHANGED";
    public const string DatabaseError = "DATABASE_ERROR";
    public const string SecurityEvent = "SECURITY_EVENT";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string IntegrityFailure = "INTEGRITY_FAILURE";
}

public static class AuditResults
{
    public const string Success = "SUCCESS";
    public const string Failure = "FAILURE";
    public const string Denied = "DENIED";
}

public sealed record AuditRecord(
    string Action,
    string Result,
    string? Entity = null,
    string? EntityId = null,
    string? Reason = null,
    int? UserId = null,
    string? Username = null);

public interface IAuditService
{
    Task WriteAsync(AuditRecord record, CancellationToken ct = default);
    void Write(AuditRecord record);
    Task<IReadOnlyList<AuditEntry>> ReadAsync(int take, CancellationToken ct = default);
    Task<bool> VerifyChainAsync(CancellationToken ct = default);
}

public interface IAppIdentity
{
    string AppVersion { get; }
    string DeviceId { get; }
}

public sealed class AppIdentity : IAppIdentity
{
    public AppIdentity(string appVersion, string deviceId)
    {
        AppVersion = appVersion;
        DeviceId = deviceId;
    }

    public string AppVersion { get; }
    public string DeviceId { get; }
}

public sealed class AuditService : IAuditService
{
    static readonly string[] Sensitive =
    {
        "password", "parol", "passwd", "pwd", "secret", "token", "apikey", "api_key",
        "key=", "authorization", "bearer", "hash", "salt", "connectionstring",
        "data source", "pragma key", "sqlcipher",
    };

    const int MaxReason = 400;

    readonly IDbContextFactory _factory;
    readonly ISessionStore _sessions;
    readonly IAppIdentity _identity;
    readonly IClock _clock;
    readonly byte[] _chainKey;
    readonly SemaphoreSlim _gate = new(1, 1);

    public AuditService(
        IDbContextFactory factory,
        ISessionStore sessions,
        IAppIdentity identity,
        ISecretKeyProvider keys,
        IClock? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _clock = clock ?? SystemClock.Instance;
        ArgumentNullException.ThrowIfNull(keys);
        _chainKey = keys.GetOrCreate(SecretNames.AuditChainKey, 32);
    }

    public void Write(AuditRecord record)
    {
        try
        {
            WriteAsync(record).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }
    }

    public async Task WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = _factory.Create();

            var principal = _sessions.Current;
            var entry = new AuditEntry
            {
                TimestampUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                UserId = record.UserId ?? principal?.UserId,
                Username = Truncate(record.Username ?? principal?.Username, 64),
                Action = Truncate(record.Action, 64) ?? AuditActions.SecurityEvent,
                Entity = Truncate(record.Entity, 64),
                EntityId = Truncate(record.EntityId, 64),
                Result = Truncate(record.Result, 32) ?? AuditResults.Failure,
                Reason = Redact(record.Reason),
                AppVersion = Truncate(_identity.AppVersion, 32),
                DeviceId = Truncate(_identity.DeviceId, 64),
                SessionId = Truncate(principal?.SessionId, 64),
            };

            var previous = await db.AuditEntries
                .AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Select(x => x.Chain)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            entry.PrevChain = previous;
            entry.Chain = ComputeChain(entry, previous);

            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAsync(int take, CancellationToken ct = default)
    {
        if (take is < 1 or > 5000) take = 200;
        await using var db = _factory.Create();
        return await db.AuditEntries
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> VerifyChainAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        string? previous = null;

        var entries = db.AuditEntries.AsNoTracking().OrderBy(x => x.Id).AsAsyncEnumerable();
        await foreach (var entry in entries.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(entry.PrevChain, previous, StringComparison.Ordinal)) return false;
            var expected = ComputeChain(entry, previous);
            if (!string.Equals(entry.Chain, expected, StringComparison.Ordinal)) return false;
            previous = entry.Chain;
        }

        return true;
    }

    string ComputeChain(AuditEntry entry, string? previous)
    {
        var sb = new StringBuilder();
        Append(sb, previous);
        Append(sb, entry.TimestampUtc);
        Append(sb, entry.UserId?.ToString(CultureInfo.InvariantCulture));
        Append(sb, entry.Username);
        Append(sb, entry.Action);
        Append(sb, entry.Entity);
        Append(sb, entry.EntityId);
        Append(sb, entry.Result);
        Append(sb, entry.Reason);
        Append(sb, entry.AppVersion);
        Append(sb, entry.DeviceId);
        Append(sb, entry.SessionId);
        var mac = HMACSHA256.HashData(_chainKey, Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(mac);
    }

    static void Append(StringBuilder sb, string? value)
    {
        var v = value ?? string.Empty;
        sb.Append(v.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v).Append(';');
    }

    internal static string? Redact(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var trimmed = reason.Length > MaxReason ? reason[..MaxReason] : reason;
        var lower = trimmed.ToLowerInvariant();
        foreach (var marker in Sensitive)
            if (lower.Contains(marker, StringComparison.Ordinal))
                return "[redacted]";

        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString();
    }

    static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
