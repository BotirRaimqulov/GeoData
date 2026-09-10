using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPro.Core.Security;

public static class SqlIdentifier
{
    const int MaxLength = 64;

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "drop", "alter", "create", "attach",
        "detach", "pragma", "union", "join", "where", "from", "table", "index",
        "vacuum", "trigger", "view", "exec", "execute", "grant", "revoke",
    };

    public static bool IsValid(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        if (identifier.Length > MaxLength) return false;
        if (!char.IsAsciiLetter(identifier[0]) && identifier[0] != '_') return false;
        foreach (var c in identifier)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        return !Reserved.Contains(identifier);
    }

    public static string Quote(string identifier)
    {
        if (!IsValid(identifier)) throw new ArgumentException(null, nameof(identifier));
        return "\"" + identifier + "\"";
    }

    public static string QuoteFrom(string identifier, IReadOnlyCollection<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (!IsValid(identifier)) throw new ArgumentException(null, nameof(identifier));
        if (!allowed.Contains(identifier, StringComparer.Ordinal)) throw new ArgumentException(null, nameof(identifier));
        return "\"" + identifier + "\"";
    }

    static readonly string[] AllowedTypes = { "INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC" };
    static readonly string[] AllowedDefaults = { "0", "1", "''" };

    public static string ColumnType(string declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration)) throw new ArgumentException(null, nameof(declaration));
        if (declaration.Length > 48) throw new ArgumentException(null, nameof(declaration));
        foreach (var c in declaration)
            if (!char.IsAsciiLetterOrDigit(c) && c != ' ' && c != '_' && c != '\'')
                throw new ArgumentException(null, nameof(declaration));

        var tokens = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 5) throw new ArgumentException(null, nameof(declaration));

        if (!AllowedTypes.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(null, nameof(declaration));

        int i = 1;
        while (i < tokens.Length &&
               (tokens[i].Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
                tokens[i].Equals("NOT", StringComparison.OrdinalIgnoreCase)))
            i++;

        if (i < tokens.Length)
        {
            if (!tokens[i].Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(null, nameof(declaration));
            i++;
            if (i >= tokens.Length || !AllowedDefaults.Contains(tokens[i], StringComparer.Ordinal))
                throw new ArgumentException(null, nameof(declaration));
            i++;
        }

        if (i != tokens.Length) throw new ArgumentException(null, nameof(declaration));

        return string.Join(' ', tokens.Select(t => t == "''" ? t : t.ToUpperInvariant()));
    }

    public static string OrderDirection(string? direction) =>
        string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
}
