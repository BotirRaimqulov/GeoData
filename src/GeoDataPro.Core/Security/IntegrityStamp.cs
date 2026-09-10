using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeoDataPro.Core.Security;

public interface IIntegrityStamper
{
    string Compute(string entity, object? id, params object?[] fields);
    bool Verify(string? stamp, string entity, object? id, params object?[] fields);
}

public sealed class HmacIntegrityStamper : IIntegrityStamper
{
    readonly byte[] _key;

    public HmacIntegrityStamper(ISecretKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _key = keys.GetOrCreate(SecretNames.IntegrityKey, 32);
    }

    public string Compute(string entity, object? id, params object?[] fields)
    {
        var canonical = Canonicalize(entity, id, fields);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToBase64String(HMACSHA256.HashData(_key, bytes));
    }

    public bool Verify(string? stamp, string entity, object? id, params object?[] fields)
    {
        if (string.IsNullOrEmpty(stamp)) return false;

        byte[] provided;
        try { provided = Convert.FromBase64String(stamp); }
        catch (FormatException) { return false; }

        var expected = Convert.FromBase64String(Compute(entity, id, fields));
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    internal static string Canonicalize(string entity, object? id, object?[] fields)
    {
        var sb = new StringBuilder();
        Append(sb, entity);
        Append(sb, Format(id));
        foreach (var field in fields) Append(sb, Format(field));
        return sb.ToString();
    }

    static void Append(StringBuilder sb, string value) =>
        sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');

    static string Format(object? value) => value switch
    {
        null => "~",
        string s => "s" + s,
        bool b => "b" + (b ? "1" : "0"),
        double d => "d" + d.ToString("R", CultureInfo.InvariantCulture),
        float f => "d" + ((double)f).ToString("R", CultureInfo.InvariantCulture),
        decimal m => "d" + ((double)m).ToString("R", CultureInfo.InvariantCulture),
        DateTimeOffset dto => "t" + dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => "t" + dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Guid g => "g" + g.ToString("N"),
        byte[] raw => "x" + Convert.ToBase64String(raw),
        IFormattable f => "n" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => "o" + value.ToString(),
    };
}
