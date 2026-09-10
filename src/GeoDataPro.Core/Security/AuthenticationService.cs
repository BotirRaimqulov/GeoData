using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.Core.Security;

public enum AuthOutcome
{
    Success = 0,
    InvalidCredentials,
    LockedOut,
    Throttled,
    Disabled,
    PasswordRejected,
    NotAuthenticated,
    Conflict,
}

public sealed record AuthResult(AuthOutcome Outcome, Principal? Principal = null, TimeSpan RetryAfter = default, PasswordRejection Rejection = PasswordRejection.None)
{
    public bool Succeeded => Outcome == AuthOutcome.Success;
}

public interface IAuthenticationService
{
    Task<AuthResult> LoginAsync(string? username, string? password, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<AuthResult> ChangePasswordAsync(string? currentPassword, string? newPassword, CancellationToken ct = default);
    Task<bool> HasAnyUserAsync(CancellationToken ct = default);
    Task<AuthResult> ProvisionFirstAdminAsync(string username, string displayName, string password, CancellationToken ct = default);
}

public sealed class AuthenticationService : IAuthenticationService
{
    public const int MaxFailedAttempts = 8;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionLifetime = SessionStore.AbsoluteTimeout;

    readonly IDbContextFactory _factory;
    readonly IPasswordHasher _hasher;
    readonly PasswordPolicy _policy;
    readonly ILoginThrottle _throttle;
    readonly ISessionStore _sessions;
    readonly IAuditService _audit;
    readonly IClock _clock;

    public AuthenticationService(
        IDbContextFactory factory,
        IPasswordHasher hasher,
        PasswordPolicy policy,
        ILoginThrottle throttle,
        ISessionStore sessions,
        IAuditService audit,
        IClock? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? SystemClock.Instance;
    }

    public static string Normalize(string? username) =>
        (username ?? string.Empty).Trim().ToUpperInvariant();

    public async Task<AuthResult> LoginAsync(string? username, string? password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var now = _clock.UtcNow;

        if (normalized.Length is 0 or > 64 || string.IsNullOrEmpty(password) || password.Length > PasswordPolicy.MaxLength)
        {
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.LoginFailed, AuditResults.Failure, "User", null, "input", Username: Safe(normalized)), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        if (!_throttle.TryEnter(normalized, out var retryAfter))
        {
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.LoginBlocked, AuditResults.Denied, "User", null, "throttle", Username: Safe(normalized)), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.Throttled, RetryAfter: retryAfter);
        }

        await using var db = _factory.Create();
        var user = await db.Users
            .FirstOrDefaultAsync(x => x.UsernameNormalized == normalized, ct)
            .ConfigureAwait(false);

        var stored = user?.PasswordHash;
        var verified = _hasher.Verify(password, stored);

        if (user == null || !verified)
        {
            _throttle.OnFailure(normalized);

            if (user != null)
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= MaxFailedAttempts)
                    user.LockoutEndUtc = (now + LockoutDuration).ToString("O", CultureInfo.InvariantCulture);
                user.UpdatedUtc = now.ToString("O", CultureInfo.InvariantCulture);
                await TrySaveAsync(db, ct).ConfigureAwait(false);
            }

            await _audit.WriteAsync(new AuditRecord(
                AuditActions.LoginFailed, AuditResults.Failure, "User", user?.Id.ToString(CultureInfo.InvariantCulture),
                "credentials", user?.Id, Safe(normalized)), ct).ConfigureAwait(false);

            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.LoginFailed, AuditResults.Denied, "User",
                user.Id.ToString(CultureInfo.InvariantCulture), "disabled", user.Id, Safe(normalized)), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.Disabled);
        }

        if (IsLockedOut(user, now, out var lockRemaining))
        {
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.LoginBlocked, AuditResults.Denied, "User",
                user.Id.ToString(CultureInfo.InvariantCulture), "lockout", user.Id, Safe(normalized)), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.LockedOut, RetryAfter: lockRemaining);
        }

        if (!RolePermissions.IsDefined((AppRole)user.Role))
        {
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.SecurityEvent, AuditResults.Denied, "User",
                user.Id.ToString(CultureInfo.InvariantCulture), "role", user.Id, Safe(normalized)), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.Disabled);
        }

        _throttle.OnSuccess(normalized);

        user.FailedAttempts = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = now.ToString("O", CultureInfo.InvariantCulture);
        user.UpdatedUtc = now.ToString("O", CultureInfo.InvariantCulture);
        if (_hasher.NeedsUpgrade(user.PasswordHash))
            user.PasswordHash = _hasher.Hash(password);
        await TrySaveAsync(db, ct).ConfigureAwait(false);

        var principal = new Principal(
            user.Id,
            user.Username,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            (AppRole)user.Role,
            SessionStore.NewSessionId(),
            now,
            now + SessionLifetime,
            user.MustChangePassword);

        _sessions.Begin(principal);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.LoginSuccess, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), null, user.Id, user.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success, principal);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var principal = _sessions.Current;
        _sessions.Clear();

        if (principal != null)
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.Logout, AuditResults.Success, "User",
                principal.UserId.ToString(CultureInfo.InvariantCulture), null, principal.UserId, principal.Username), ct)
                .ConfigureAwait(false);
    }

    public async Task<AuthResult> ChangePasswordAsync(string? currentPassword, string? newPassword, CancellationToken ct = default)
    {
        var principal = _sessions.Current;
        if (principal == null) return new AuthResult(AuthOutcome.NotAuthenticated);

        await using var db = _factory.Create();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == principal.UserId, ct).ConfigureAwait(false);
        if (user == null || !user.IsActive) return new AuthResult(AuthOutcome.Disabled);

        if (!_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash))
        {
            _throttle.OnFailure(user.UsernameNormalized);
            await _audit.WriteAsync(new AuditRecord(
                AuditActions.PasswordChanged, AuditResults.Failure, "User",
                user.Id.ToString(CultureInfo.InvariantCulture), "credentials", user.Id, user.Username), ct)
                .ConfigureAwait(false);
            return new AuthResult(AuthOutcome.InvalidCredentials);
        }

        var check = _policy.Validate(newPassword, user.Username, user.DisplayName);
        if (!check.Accepted)
            return new AuthResult(AuthOutcome.PasswordRejected, Rejection: check.Reason);

        if (_hasher.Verify(newPassword!, user.PasswordHash))
            return new AuthResult(AuthOutcome.PasswordRejected, Rejection: PasswordRejection.Repetitive);

        user.PasswordHash = _hasher.Hash(newPassword!);
        user.MustChangePassword = false;
        user.UpdatedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _sessions.Begin(principal with { MustChangePassword = false });

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.PasswordChanged, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), null, user.Id, user.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success, _sessions.Current);
    }

    public async Task<bool> HasAnyUserAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        return await db.Users.AnyAsync(ct).ConfigureAwait(false);
    }

    public async Task<AuthResult> ProvisionFirstAdminAsync(string username, string displayName, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        if (normalized.Length is 0 or > 64) return new AuthResult(AuthOutcome.InvalidCredentials);

        var check = _policy.Validate(password, username, displayName);
        if (!check.Accepted) return new AuthResult(AuthOutcome.PasswordRejected, Rejection: check.Reason);

        await using var db = _factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (await db.Users.AnyAsync(ct).ConfigureAwait(false))
            return new AuthResult(AuthOutcome.Conflict);

        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var user = new AppUser
        {
            Username = username.Trim(),
            UsernameNormalized = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
            PasswordHash = _hasher.Hash(password),
            Role = (int)AppRole.Admin,
            IsActive = true,
            MustChangePassword = false,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.UserCreated, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), "bootstrap", user.Id, user.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    static bool IsLockedOut(AppUser user, DateTimeOffset now, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (string.IsNullOrEmpty(user.LockoutEndUtc)) return false;
        if (!DateTimeOffset.TryParse(user.LockoutEndUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var until)) return false;
        if (until <= now) return false;
        remaining = until - now;
        return true;
    }

    static async Task TrySaveAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
        }
    }

    static string Safe(string normalized) => normalized.Length <= 64 ? normalized : normalized[..64];
}
