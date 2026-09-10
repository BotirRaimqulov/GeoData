using System;

namespace GeoDataPro.Core.Data;

public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string UsernameNormalized { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedAttempts { get; set; }
    public string? LockoutEndUtc { get; set; }
    public string? LastLoginUtc { get; set; }
    public string CreatedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");
    public string? Stamp { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public string TimestampUtc { get; set; } = "";
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string Action { get; set; } = "";
    public string? Entity { get; set; }
    public string? EntityId { get; set; }
    public string Result { get; set; } = "";
    public string? Reason { get; set; }
    public string? AppVersion { get; set; }
    public string? DeviceId { get; set; }
    public string? SessionId { get; set; }
    public string? PrevChain { get; set; }
    public string? Chain { get; set; }
}

public class SecurityFlag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public string UpdatedUtc { get; set; } = "";
}

public class DeletedRecord
{
    public long Id { get; set; }
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Payload { get; set; } = "";
    public string DeletedUtc { get; set; } = "";
    public int? DeletedByUserId { get; set; }
    public string? Stamp { get; set; }
    public bool Restored { get; set; }
}
