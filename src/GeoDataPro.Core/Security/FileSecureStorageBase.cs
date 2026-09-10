using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GeoDataPro.Core.Security;

public abstract class FileSecureStorageBase : ISecureStorage
{
    readonly string _directory;
    readonly object _gate = new();

    protected FileSecureStorageBase(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException(null, nameof(directory));
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        Harden(_directory);
    }

    public abstract string Kind { get; }

    protected abstract byte[] Seal(string name, ReadOnlySpan<byte> value);

    protected abstract bool TryUnseal(string name, byte[] sealedValue, out byte[] value);

    protected virtual void Harden(string path) { }

    public bool TryRead(string name, out byte[] value)
    {
        var file = Resolve(name);
        lock (_gate)
        {
            try
            {
                if (!File.Exists(file))
                {
                    value = Array.Empty<byte>();
                    return false;
                }

                var raw = File.ReadAllBytes(file);
                return TryUnseal(name, raw, out value);
            }
            catch (IOException)
            {
                value = Array.Empty<byte>();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                value = Array.Empty<byte>();
                return false;
            }
        }
    }

    public void Write(string name, ReadOnlySpan<byte> value)
    {
        var file = Resolve(name);
        var payload = Seal(name, value);
        lock (_gate)
        {
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, payload);
            Harden(temp);
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
            Harden(file);
        }
    }

    public void Delete(string name)
    {
        var file = Resolve(name);
        lock (_gate)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32) throw new ArgumentException(null, nameof(name));
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
                throw new ArgumentException(null, nameof(name));

        var full = Path.GetFullPath(Path.Combine(_directory, name + ".bin"));
        if (!full.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException(null, nameof(name));
        return full;
    }

    protected static byte[] Context(string kind, string name) =>
        Encoding.UTF8.GetBytes(kind + "|" + name);
}
