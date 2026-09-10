using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.Core.Security;

public sealed record UserSummary(
    int Id,
    string Username,
    string DisplayName,
    AppRole Role,
    bool IsActive,
    bool MustChangePassword,
    bool IsLockedOut,
    string? LastLoginUtc);

public interface IUserAdminService
{
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct = default);
    Task<AuthResult> CreateAsync(string username, string displayName, string password, AppRole role, CancellationToken ct = default);
    Task<AuthResult> SetRoleAsync(int userId, AppRole role, CancellationToken ct = default);
    Task<AuthResult> SetActiveAsync(int userId, bool active, CancellationToken ct = default);
    Task<AuthResult> ResetPasswordAsync(int userId, string password, CancellationToken ct = default);
    Task<AuthResult> UnlockAsync(int userId, CancellationToken ct = default);
}

public sealed class UserAdminService : IUserAdminService
{
    readonly IDbContextFactory _factory;
    readonly IAuthorizationService _authz;
    readonly IPasswordHasher _hasher;
    readonly PasswordPolicy _policy;
    readonly IAuditService _audit;
    readonly IClock _clock;

    public UserAdminService(
        IDbContextFactory factory,
        IAuthorizationService authz,
        IPasswordHasher hasher,
        PasswordPolicy policy,
        IAuditService audit,
        IClock? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _authz = authz ?? throw new ArgumentNullException(nameof(authz));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct = default)
    {
        _authz.Require(Permissions.UserManage);

        await using var db = _factory.Create();
        var now = _clock.UtcNow;
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync(ct).ConfigureAwait(false);

        return users.Select(x => new UserSummary(
            x.Id,
            x.Username,
            x.DisplayName,
            (AppRole)x.Role,
            x.IsActive,
            x.MustChangePassword,
            LockedOut(x.LockoutEndUtc, now),
            x.LastLoginUtc)).ToList();
    }

    public async Task<AuthResult> CreateAsync(string username, string displayName, string password, AppRole role, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.UserManage);

        var normalized = AuthenticationService.Normalize(username);
        if (normalized.Length is 0 or > 64) return new AuthResult(AuthOutcome.InvalidCredentials);
        if (!RolePermissions.IsDefined(role)) return new AuthResult(AuthOutcome.InvalidCredentials);

        var check = _policy.Validate(password, username, displayName);
        if (!check.Accepted) return new AuthResult(AuthOutcome.PasswordRejected, Rejection: check.Reason);

        await using var db = _factory.Create();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (await db.Users.AnyAsync(x => x.UsernameNormalized == normalized, ct).ConfigureAwait(false))
            return new AuthResult(AuthOutcome.Conflict);

        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var user = new AppUser
        {
            Username = username.Trim(),
            UsernameNormalized = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
            PasswordHash = _hasher.Hash(password),
            Role = (int)role,
            IsActive = true,
            MustChangePassword = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.UserCreated, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), role.ToString(), actor.UserId, actor.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    public async Task<AuthResult> SetRoleAsync(int userId, AppRole role, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.UserManage);
        if (!RolePermissions.IsDefined(role)) return new AuthResult(AuthOutcome.InvalidCredentials);

        await using var db = _factory.Create();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        if (user == null) return new AuthResult(AuthOutcome.InvalidCredentials);

        if (user.Role == (int)AppRole.Admin && role != AppRole.Admin &&
            await CountActiveAdminsAsync(db, ct).ConfigureAwait(false) <= 1)
            return new AuthResult(AuthOutcome.Conflict);

        user.Role = (int)role;
        user.UpdatedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.RoleChanged, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), role.ToString(), actor.UserId, actor.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    public async Task<AuthResult> SetActiveAsync(int userId, bool active, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.UserManage);

        await using var db = _factory.Create();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        if (user == null) return new AuthResult(AuthOutcome.InvalidCredentials);

        if (!active && user.Role == (int)AppRole.Admin &&
            await CountActiveAdminsAsync(db, ct).ConfigureAwait(false) <= 1)
            return new AuthResult(AuthOutcome.Conflict);

        user.IsActive = active;
        user.UpdatedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.UserUpdated, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), active ? "enabled" : "disabled",
            actor.UserId, actor.Username), ct).ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    public async Task<AuthResult> ResetPasswordAsync(int userId, string password, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.UserManage);

        await using var db = _factory.Create();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        if (user == null) return new AuthResult(AuthOutcome.InvalidCredentials);

        var check = _policy.Validate(password, user.Username, user.DisplayName);
        if (!check.Accepted) return new AuthResult(AuthOutcome.PasswordRejected, Rejection: check.Reason);

        user.PasswordHash = _hasher.Hash(password);
        user.MustChangePassword = true;
        user.FailedAttempts = 0;
        user.LockoutEndUtc = null;
        user.UpdatedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.PasswordChanged, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), "reset", actor.UserId, actor.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    public async Task<AuthResult> UnlockAsync(int userId, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.UserManage);

        await using var db = _factory.Create();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        if (user == null) return new AuthResult(AuthOutcome.InvalidCredentials);

        user.FailedAttempts = 0;
        user.LockoutEndUtc = null;
        user.UpdatedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditRecord(
            AuditActions.UserUpdated, AuditResults.Success, "User",
            user.Id.ToString(CultureInfo.InvariantCulture), "unlock", actor.UserId, actor.Username), ct)
            .ConfigureAwait(false);

        return new AuthResult(AuthOutcome.Success);
    }

    static Task<int> CountActiveAdminsAsync(AppDbContext db, CancellationToken ct) =>
        db.Users.CountAsync(x => x.IsActive && x.Role == (int)AppRole.Admin, ct);

    static bool LockedOut(string? until, DateTimeOffset now) =>
        !string.IsNullOrEmpty(until) &&
        DateTimeOffset.TryParse(until, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) &&
        end > now;
}
