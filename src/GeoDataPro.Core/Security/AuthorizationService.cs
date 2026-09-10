using System;

namespace GeoDataPro.Core.Security;

public interface IAuthorizationService
{
    bool Has(string permission);
    void Demand(string permission);
    Principal Require(string permission);
}

public sealed class AuthorizationService : IAuthorizationService
{
    readonly ISessionStore _sessions;

    public AuthorizationService(ISessionStore sessions) =>
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    public bool Has(string permission)
    {
        if (string.IsNullOrEmpty(permission)) return false;
        var principal = _sessions.Current;
        return principal != null && principal.Has(permission);
    }

    public void Demand(string permission) => Require(permission);

    public Principal Require(string permission)
    {
        if (string.IsNullOrEmpty(permission)) throw new ArgumentException(null, nameof(permission));

        var principal = _sessions.Current;
        if (principal == null) throw new NotAuthenticatedException(permission);
        if (!principal.Has(permission)) throw new SecurityDeniedException(permission);

        _sessions.Touch();
        return principal;
    }
}
