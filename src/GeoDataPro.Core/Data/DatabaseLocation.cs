using System;
using System.IO;
using GeoDataPro.Core.Security;
using Microsoft.Data.Sqlite;

namespace GeoDataPro.Core.Data;

public interface IPlatformPaths
{
    string DataDirectory { get; }
    string KeyDirectory { get; }
    string BackupDirectory { get; }
    string LogDirectory { get; }
    void Harden(string path);
}

public sealed class DefaultPlatformPaths : IPlatformPaths
{
    const string Vendor = "GeoDataPro";

    public DefaultPlatformPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Vendor);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KeyDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string Root { get; }
    public string DataDirectory => Path.Combine(Root, "data");
    public string KeyDirectory => Path.Combine(Root, "k");
    public string BackupDirectory => Path.Combine(Root, "backup");
    public string LogDirectory => Path.Combine(Root, "log");

    public void Harden(string path) { }
}

public static class DatabaseLocation
{
    static readonly object Gate = new();
    static IPlatformPaths? _paths;
    static ISecretKeyProvider? _keys;
    static string? _override;

    public static void Configure(IPlatformPaths paths, ISecretKeyProvider keys)
    {
        lock (Gate)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _override = null;
        }
    }

    public static void ConfigureOverride(string connectionString)
    {
        lock (Gate) _override = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public static bool IsConfigured
    {
        get { lock (Gate) return _override != null || (_paths != null && _keys != null); }
    }

    public static IPlatformPaths Paths
    {
        get
        {
            lock (Gate)
            {
                _paths ??= new DefaultPlatformPaths();
                return _paths;
            }
        }
    }

    public static string DatabaseFile => Path.Combine(Paths.DataDirectory, "geodata.db");

    public static string LegacyDatabaseFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoDataPro",
        "geodata.db");

    public static string BuildConnectionString()
    {
        lock (Gate)
        {
            if (_override != null) return _override;
            if (_keys == null) throw new SecretUnavailableException(SecretNames.DatabaseKey);
        }

        return BuildConnectionString(DatabaseFile, CurrentKeyMaterial());
    }

    public static string BuildConnectionString(string file, string keyMaterial) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
            Password = keyMaterial,
        }.ToString();

    public static string CurrentKeyMaterial()
    {
        ISecretKeyProvider keys;
        lock (Gate)
        {
            if (_keys == null) throw new SecretUnavailableException(SecretNames.DatabaseKey);
            keys = _keys;
        }

        var raw = keys.GetOrCreate(SecretNames.DatabaseKey, 32);
        try
        {
            return Convert.ToHexString(raw).ToLowerInvariant();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _paths = null;
            _keys = null;
            _override = null;
        }
    }
}
