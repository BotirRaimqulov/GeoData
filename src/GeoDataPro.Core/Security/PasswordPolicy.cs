using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPro.Core.Security;

public enum PasswordRejection
{
    None = 0,
    TooShort,
    TooLong,
    NotComplex,
    ContainsIdentity,
    Common,
    Repetitive,
}

public sealed record PasswordCheck(bool Accepted, PasswordRejection Reason)
{
    public static readonly PasswordCheck Ok = new(true, PasswordRejection.None);
    public static PasswordCheck Fail(PasswordRejection reason) => new(false, reason);
}

public sealed class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 256;
    const int RequiredClasses = 3;

    static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passw0rd", "parol", "parol123", "qwerty", "qwerty123", "qwertyuiop",
        "123456", "1234567", "12345678", "123456789", "1234567890", "111111", "000000",
        "admin", "admin123", "administrator", "welcome", "welcome1", "letmein",
        "iloveyou", "monkey", "dragon", "sunshine", "princess", "football",
        "geodata", "geodata123", "geodatapro", "geolog", "geologist",
        "changeme", "default", "secret", "master", "root", "toor", "test", "testtest",
    };

    public PasswordCheck Validate(string? password, string? username = null, string? displayName = null)
    {
        if (password is null) return PasswordCheck.Fail(PasswordRejection.TooShort);
        if (password.Length < MinLength) return PasswordCheck.Fail(PasswordRejection.TooShort);
        if (password.Length > MaxLength) return PasswordCheck.Fail(PasswordRejection.TooLong);

        int classes = 0;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes < RequiredClasses) return PasswordCheck.Fail(PasswordRejection.NotComplex);

        if (Blocked.Contains(password)) return PasswordCheck.Fail(PasswordRejection.Common);
        var stripped = new string(password.Where(char.IsLetterOrDigit).ToArray());
        if (stripped.Length >= 6 && Blocked.Contains(stripped)) return PasswordCheck.Fail(PasswordRejection.Common);

        if (Contains(password, username) || Contains(password, displayName))
            return PasswordCheck.Fail(PasswordRejection.ContainsIdentity);

        if (password.Distinct().Count() <= 3) return PasswordCheck.Fail(PasswordRejection.Repetitive);

        return PasswordCheck.Ok;
    }

    static bool Contains(string password, string? identity) =>
        !string.IsNullOrWhiteSpace(identity) && identity.Trim().Length >= 3 &&
        password.Contains(identity.Trim(), StringComparison.OrdinalIgnoreCase);
}
