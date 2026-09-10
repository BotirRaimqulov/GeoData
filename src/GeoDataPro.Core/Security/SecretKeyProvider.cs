using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public sealed class SecretKeyProvider : ISecretKeyProvider
{
    readonly ISecureStorage _storage;
    readonly object _gate = new();
    readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    public SecretKeyProvider(ISecureStorage storage) =>
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    public byte[] GetOrCreate(string name, int sizeBytes)
    {
        Validate(name, sizeBytes);
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out var cached)) return Copy(cached);

            if (_storage.TryRead(name, out var stored) && stored.Length == sizeBytes)
            {
                _cache[name] = stored;
                return Copy(stored);
            }

            var created = RandomNumberGenerator.GetBytes(sizeBytes);
            _storage.Write(name, created);
            _cache[name] = created;
            return Copy(created);
        }
    }

    public bool TryGet(string name, out byte[] key)
    {
        Validate(name, 1);
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out var cached))
            {
                key = Copy(cached);
                return true;
            }

            if (_storage.TryRead(name, out var stored))
            {
                _cache[name] = stored;
                key = Copy(stored);
                return true;
            }
        }

        key = Array.Empty<byte>();
        return false;
    }

    public void Rotate(string name, int sizeBytes)
    {
        Validate(name, sizeBytes);
        lock (_gate)
        {
            var created = RandomNumberGenerator.GetBytes(sizeBytes);
            _storage.Write(name, created);
            if (_cache.TryGetValue(name, out var old)) CryptographicOperations.ZeroMemory(old);
            _cache[name] = created;
        }
    }

    public void Remove(string name)
    {
        Validate(name, 1);
        lock (_gate)
        {
            if (_cache.Remove(name, out var old)) CryptographicOperations.ZeroMemory(old);
            _storage.Delete(name);
        }
    }

    static void Validate(string name, int sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(null, nameof(name));
        if (name.Length > 32) throw new ArgumentException(null, nameof(name));
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
                throw new ArgumentException(null, nameof(name));
        if (sizeBytes is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
    }

    static byte[] Copy(byte[] source)
    {
        var copy = new byte[source.Length];
        Buffer.BlockCopy(source, 0, copy, 0, source.Length);
        return copy;
    }
}
