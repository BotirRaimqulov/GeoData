using System;

namespace GeoDataPro.Core.Security;

public interface ISecureStorage
{
    string Kind { get; }
    bool TryRead(string name, out byte[] value);
    void Write(string name, ReadOnlySpan<byte> value);
    void Delete(string name);
}

public interface ISecretKeyProvider
{
    byte[] GetOrCreate(string name, int sizeBytes);
    bool TryGet(string name, out byte[] key);
    void Rotate(string name, int sizeBytes);
    void Remove(string name);
}

public static class SecretNames
{
    public const string DatabaseKey = "dbk";
    public const string IntegrityKey = "itg";
    public const string BackupKey = "bkp";
    public const string AuditChainKey = "adc";
}
