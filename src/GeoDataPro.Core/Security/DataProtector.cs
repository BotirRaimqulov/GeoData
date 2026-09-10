using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GeoDataPro.Core.Security;

public static class DataProtector
{
    public const int KeySize = 32;
    const int NonceSize = 12;
    const int TagSize = 16;
    const byte Version = 1;

    public static byte[] Protect(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize) throw new ArgumentException(null, nameof(key));

        var output = new byte[1 + NonceSize + TagSize + plaintext.Length];
        output[0] = Version;

        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        var tag = output.AsSpan(1 + NonceSize, TagSize);
        var cipher = output.AsSpan(1 + NonceSize + TagSize);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag, associatedData);
        return output;
    }

    public static bool TryUnprotect(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> associatedData,
        out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();
        if (key.Length != KeySize) return false;
        if (payload.Length < 1 + NonceSize + TagSize) return false;
        if (payload[0] != Version) return false;

        var nonce = payload.Slice(1, NonceSize);
        var tag = payload.Slice(1 + NonceSize, TagSize);
        var cipher = payload[(1 + NonceSize + TagSize)..];
        var buffer = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, buffer, associatedData);
            plaintext = buffer;
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(buffer);
            return false;
        }
        catch (ArgumentException)
        {
            CryptographicOperations.ZeroMemory(buffer);
            return false;
        }
    }

    public static byte[] Unprotect(ReadOnlySpan<byte> key, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> associatedData)
        => TryUnprotect(key, payload, associatedData, out var plaintext)
            ? plaintext
            : throw new CryptographicException("E_CRYPTO");

    public static byte[] Derive(ReadOnlySpan<byte> master, string label, int length)
    {
        if (master.Length == 0) throw new ArgumentException(null, nameof(master));
        if (length is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(length));
        var info = System.Text.Encoding.UTF8.GetBytes(label);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, master.ToArray(), length, salt: null, info: info);
    }
}
