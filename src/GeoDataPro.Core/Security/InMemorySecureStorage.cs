using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public sealed class InMemorySecureStorage : ISecureStorage
{
    readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);
    readonly object _gate = new();

    public string Kind => "mem";

    public bool TryRead(string name, out byte[] value)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(name, out var stored))
            {
                value = (byte[])stored.Clone();
                return true;
            }
        }

        value = Array.Empty<byte>();
        return false;
    }

    public void Write(string name, ReadOnlySpan<byte> value)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(name, out var old)) CryptographicOperations.ZeroMemory(old);
            _items[name] = value.ToArray();
        }
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            if (_items.Remove(name, out var old)) CryptographicOperations.ZeroMemory(old);
        }
    }
}
