using System;

namespace GeoDataPro.Core.Security;

public class SecurityDeniedException : Exception
{
    public SecurityDeniedException(string permission)
        : base("E_DENIED") => Permission = permission;

    public string Permission { get; }
}

public sealed class NotAuthenticatedException : SecurityDeniedException
{
    public NotAuthenticatedException(string permission) : base(permission) { }
}

public sealed class SessionExpiredException : SecurityDeniedException
{
    public SessionExpiredException(string permission) : base(permission) { }
}

public sealed class IntegrityViolationException : Exception
{
    public IntegrityViolationException(string entity, object? id)
        : base("E_INTEGRITY") { Entity = entity; Id = id; }

    public string Entity { get; }
    public object? Id { get; }
}

public sealed class SecretUnavailableException : Exception
{
    public SecretUnavailableException(string name) : base("E_SECRET") => Name = name;
    public string Name { get; }
}
