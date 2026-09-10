using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public sealed record Principal(
    int UserId,
    string Username,
    string DisplayName,
    AppRole Role,
    string SessionId,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    bool MustChangePassword)
{
    public bool Has(string permission) => RolePermissions.Grants(Role, permission);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ISessionStore
{
    Principal? Current { get; }
    event Action? Changed;
    void Begin(Principal principal);
    void Touch();
    void Clear();
    bool IsActive { get; }
}

public sealed class SessionStore : ISessionStore
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(8);

    readonly IClock _clock;
    readonly object _gate = new();
    Principal? _principal;
    DateTimeOffset _lastSeen;

    public SessionStore(IClock? clock = null) => _clock = clock ?? SystemClock.Instance;

    public event Action? Changed;

    public Principal? Current
    {
        get
        {
            lock (_gate)
            {
                if (_principal == null) return null;
                if (Expired(_clock.UtcNow))
                {
                    _principal = null;
                    RaiseChanged();
                    return null;
                }

                return _principal;
            }
        }
    }

    public bool IsActive => Current != null;

    public void Begin(Principal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        lock (_gate)
        {
            _principal = principal;
            _lastSeen = _clock.UtcNow;
        }

        RaiseChanged();
    }

    public void Touch()
    {
        lock (_gate)
        {
            if (_principal == null) return;
            if (Expired(_clock.UtcNow))
            {
                _principal = null;
                RaiseChanged();
                return;
            }

            _lastSeen = _clock.UtcNow;
        }
    }

    public void Clear()
    {
        bool had;
        lock (_gate)
        {
            had = _principal != null;
            _principal = null;
        }

        if (had) RaiseChanged();
    }

    bool Expired(DateTimeOffset now) =>
        _principal == null || now >= _principal.ExpiresUtc || now - _lastSeen >= IdleTimeout;

    void RaiseChanged()
    {
        var handler = Changed;
        handler?.Invoke();
    }

    public static string NewSessionId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
