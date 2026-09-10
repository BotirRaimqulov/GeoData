using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public sealed class PassphraseSecureStorage : FileSecureStorageBase, IDisposable
{
    const int SaltLength = 16;
    const int MemoryKib = 65536;
    const int Iterations = 3;
    const int Parallelism = 4;

    readonly byte[] _passphrase;
    readonly string _saltFile;
    byte[]? _master;
    bool _disposed;

    public PassphraseSecureStorage(string directory, string passphrase) : base(directory)
    {
        if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException(null, nameof(passphrase));
        _passphrase = Encoding.UTF8.GetBytes(passphrase);
        _saltFile = Path.Combine(Path.GetFullPath(directory), "s.bin");
    }

    public override string Kind => "pph";

    protected override byte[] Seal(string name, ReadOnlySpan<byte> value) =>
        DataProtector.Protect(Master(), value, Context(Kind, name));

    protected override bool TryUnseal(string name, byte[] sealedValue, out byte[] value) =>
        DataProtector.TryUnprotect(Master(), sealedValue, Context(Kind, name), out value);

    byte[] Master()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_master != null) return _master;

        byte[] salt;
        if (File.Exists(_saltFile))
        {
            salt = File.ReadAllBytes(_saltFile);
            if (salt.Length != SaltLength) throw new SecretUnavailableException("s");
        }
        else
        {
            salt = RandomNumberGenerator.GetBytes(SaltLength);
            File.WriteAllBytes(_saltFile, salt);
        }

        using var argon = new Argon2id(_passphrase)
        {
            Salt = salt,
            MemorySize = MemoryKib,
            Iterations = Iterations,
            DegreeOfParallelism = Parallelism,
        };
        _master = argon.GetBytes(DataProtector.KeySize);
        return _master;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_passphrase);
        if (_master != null) CryptographicOperations.ZeroMemory(_master);
        _master = null;
    }
}
