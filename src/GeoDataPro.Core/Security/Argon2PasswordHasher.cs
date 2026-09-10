using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public const int DefaultMemoryKib = 65536;
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 4;
    const int SaltLength = 16;
    const int HashLength = 32;
    const string Prefix = "$argon2id$v=19$";

    readonly int _memoryKib;
    readonly int _iterations;
    readonly int _parallelism;

    public Argon2PasswordHasher(
        int memoryKib = DefaultMemoryKib,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        if (memoryKib < 8192) throw new ArgumentOutOfRangeException(nameof(memoryKib));
        if (iterations < 2) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (parallelism < 1) throw new ArgumentOutOfRangeException(nameof(parallelism));
        _memoryKib = memoryKib;
        _iterations = iterations;
        _parallelism = parallelism;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt, _memoryKib, _iterations, _parallelism, HashLength);
        try
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{Prefix}m={_memoryKib},t={_iterations},p={_parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public bool Verify(string password, string? encoded)
    {
        if (password is null || string.IsNullOrEmpty(encoded)) return false;
        if (!TryParse(encoded, out var p)) return false;

        byte[]? candidate = null;
        try
        {
            candidate = Derive(password, p.Salt, p.MemoryKib, p.Iterations, p.Parallelism, p.Hash.Length);
            return CryptographicOperations.FixedTimeEquals(candidate, p.Hash);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (candidate != null) CryptographicOperations.ZeroMemory(candidate);
            CryptographicOperations.ZeroMemory(p.Hash);
        }
    }

    public bool NeedsUpgrade(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return true;
        if (!TryParse(encoded, out var p)) return true;
        return p.MemoryKib < _memoryKib || p.Iterations < _iterations || p.Hash.Length < HashLength;
    }

    static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int length)
    {
        var pwd = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(pwd)
            {
                Salt = salt,
                MemorySize = memoryKib,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };
            return argon.GetBytes(length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwd);
        }
    }

    readonly record struct Parsed(int MemoryKib, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);

    static bool TryParse(string encoded, out Parsed parsed)
    {
        parsed = default;
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var parts = encoded.Split('$');
        if (parts.Length != 6) return false;

        int m = 0, t = 0, p = 0;
        foreach (var seg in parts[3].Split(','))
        {
            var kv = seg.Split('=');
            if (kv.Length != 2) return false;
            if (!int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;
            switch (kv[0])
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
                default: return false;
            }
        }

        if (m < 1024 || t < 1 || p < 1) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[4]);
            var hash = Convert.FromBase64String(parts[5]);
            if (salt.Length < 8 || hash.Length < 16) return false;
            parsed = new Parsed(m, t, p, salt, hash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
