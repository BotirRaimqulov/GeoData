using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Files;
using GeoDataPro.Core.Security;
using Microsoft.Data.Sqlite;

namespace GeoDataPro.Core.Backup;

public sealed record BackupInfo(string Path, DateTimeOffset CreatedUtc, long Bytes);

public enum BackupVerification
{
    Valid = 0,
    Missing,
    Truncated,
    BadMagic,
    BadVersion,
    Tampered,
    WrongKey,
}

public interface IBackupService
{
    Task<BackupInfo> CreateAsync(CancellationToken ct = default);
    Task<BackupVerification> VerifyAsync(string path, CancellationToken ct = default);
    Task RestoreAsync(string path, CancellationToken ct = default);
    IReadOnlyList<BackupInfo> List();
    int Prune(int keep);
}

public sealed class BackupService : IBackupService
{
    public const int DefaultRetention = 10;
    const long MaxBackupBytes = 4L * 1024 * 1024 * 1024;
    const int ChunkSize = 1 << 20;

    static readonly byte[] Magic = "GDPB"u8.ToArray();
    const byte FormatVersion = 1;

    readonly IPlatformPaths _paths;
    readonly IAuthorizationService _authz;
    readonly IAuditService _audit;
    readonly ISecretKeyProvider _keys;
    readonly IClock _clock;
    readonly Func<string> _databaseFile;
    readonly Func<string> _databaseKey;

    public BackupService(
        IPlatformPaths paths,
        IAuthorizationService authz,
        IAuditService audit,
        ISecretKeyProvider keys,
        IClock? clock = null,
        Func<string>? databaseFile = null,
        Func<string>? databaseKey = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _authz = authz ?? throw new ArgumentNullException(nameof(authz));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _clock = clock ?? SystemClock.Instance;
        _databaseFile = databaseFile ?? (() => DatabaseLocation.DatabaseFile);
        _databaseKey = databaseKey ?? DatabaseLocation.CurrentKeyMaterial;
    }

    public async Task<BackupInfo> CreateAsync(CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.Backup);

        var now = _clock.UtcNow;
        var name = "b-" + now.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + ".gdb";
        var destination = SafeFile.EnsureUnder(_paths.BackupDirectory, name);

        var snapshot = Path.Combine(_paths.BackupDirectory, "." + Guid.NewGuid().ToString("N") + ".snap");
        try
        {
            CreateSnapshot(snapshot);
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(snapshot);
            if (info.Length > MaxBackupBytes) throw new InvalidOperationException("E_TOOLARGE");

            await SealAsync(snapshot, destination, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(snapshot);
        }

        var result = new FileInfo(destination);

        await _audit.WriteAsync(new AuditRecord(AuditActions.Backup, AuditResults.Success, "Database",
            Path.GetFileName(destination), null, actor.UserId, actor.Username), ct).ConfigureAwait(false);

        Prune(DefaultRetention);
        return new BackupInfo(destination, now, result.Length);
    }

    void CreateSnapshot(string snapshot)
    {
        using var source = new SqliteConnection(
            DatabaseLocation.BuildConnectionString(_databaseFile(), _databaseKey()));
        source.Open();

        using var target = new SqliteConnection(
            DatabaseLocation.BuildConnectionString(snapshot, _databaseKey()));
        target.Open();

        source.BackupDatabase(target);

        using var check = target.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        var status = check.ExecuteScalar() as string;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("E_SNAPSHOT");
    }

    async Task SealAsync(string source, string destination, CancellationToken ct)
    {
        var key = _keys.GetOrCreate(SecretNames.BackupKey, DataProtector.KeySize);
        try
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var streamKey = DataProtector.Derive(key, "backup-stream", DataProtector.KeySize);
            var macKey = DataProtector.Derive(key, "backup-mac", 32);

            try
            {
                SafeFile.WriteAtomic(destination, temp => SealTo(source, temp, salt, streamKey, macKey, ct));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(streamKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    static void SealTo(string source, string temp, byte[] salt, byte[] streamKey, byte[] macKey, CancellationToken ct)
    {
        using var input = File.OpenRead(source);
        using var output = File.Create(temp);
        using var mac = new HMACSHA256(macKey);

        var header = new byte[Magic.Length + 1 + salt.Length];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        header[Magic.Length] = FormatVersion;
        Buffer.BlockCopy(salt, 0, header, Magic.Length + 1, salt.Length);
        output.Write(header);
        mac.TransformBlock(header, 0, header.Length, null, 0);

        var buffer = new byte[ChunkSize];
        long index = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            var aad = new byte[16];
            Buffer.BlockCopy(salt, 0, aad, 0, 8);
            BitConverter.TryWriteBytes(aad.AsSpan(8), index);

            var sealedChunk = DataProtector.Protect(streamKey, buffer.AsSpan(0, read), aad);
            var length = BitConverter.GetBytes(sealedChunk.Length);

            output.Write(length);
            output.Write(sealedChunk);
            mac.TransformBlock(length, 0, length.Length, null, 0);
            mac.TransformBlock(sealedChunk, 0, sealedChunk.Length, null, 0);
            index++;
        }

        var terminator = BitConverter.GetBytes(0);
        output.Write(terminator);
        mac.TransformBlock(terminator, 0, terminator.Length, null, 0);
        mac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        output.Write(mac.Hash!);
        output.Flush(true);
    }

    public async Task<BackupVerification> VerifyAsync(string path, CancellationToken ct = default)
    {
        _authz.Require(Permissions.Backup);
        return await Task.Run(() => Unseal(path, null, ct), ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(string path, CancellationToken ct = default)
    {
        var actor = _authz.Require(Permissions.Restore);

        var staging = Path.Combine(_paths.BackupDirectory, "." + Guid.NewGuid().ToString("N") + ".rst");
        try
        {
            var verdict = await Task.Run(() => Unseal(path, staging, ct), ct).ConfigureAwait(false);
            if (verdict != BackupVerification.Valid)
            {
                await _audit.WriteAsync(new AuditRecord(AuditActions.BackupRestore, AuditResults.Failure,
                    "Database", Path.GetFileName(path), verdict.ToString(), actor.UserId, actor.Username), ct)
                    .ConfigureAwait(false);
                throw new InvalidOperationException("E_BACKUP");
            }

            using (var probe = new SqliteConnection(DatabaseLocation.BuildConnectionString(staging, _databaseKey())))
            {
                probe.Open();
                using var check = probe.CreateCommand();
                check.CommandText = "PRAGMA quick_check";
                if (check.ExecuteScalar() as string is not "ok")
                    throw new InvalidOperationException("E_BACKUP");
            }

            var target = _databaseFile();
            var rescue = target + ".pre-" + _clock.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
            if (File.Exists(target)) File.Copy(target, rescue, false);

            TryDelete(target + "-wal");
            TryDelete(target + "-shm");
            File.Copy(staging, target, true);

            await _audit.WriteAsync(new AuditRecord(AuditActions.BackupRestore, AuditResults.Success,
                "Database", Path.GetFileName(path), null, actor.UserId, actor.Username), ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    BackupVerification Unseal(string path, string? destination, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return BackupVerification.Missing;

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return BackupVerification.Missing; }

        if (!File.Exists(full)) return BackupVerification.Missing;

        var key = _keys.GetOrCreate(SecretNames.BackupKey, DataProtector.KeySize);
        byte[]? streamKey = null;
        byte[]? macKey = null;

        try
        {
            streamKey = DataProtector.Derive(key, "backup-stream", DataProtector.KeySize);
            macKey = DataProtector.Derive(key, "backup-mac", 32);

            using var input = File.OpenRead(full);
            using var mac = new HMACSHA256(macKey);

            var header = new byte[Magic.Length + 1 + 16];
            if (input.Read(header, 0, header.Length) != header.Length) return BackupVerification.Truncated;
            if (!header.Take(Magic.Length).SequenceEqual(Magic)) return BackupVerification.BadMagic;
            if (header[Magic.Length] != FormatVersion) return BackupVerification.BadVersion;
            mac.TransformBlock(header, 0, header.Length, null, 0);

            var salt = header.Skip(Magic.Length + 1).Take(16).ToArray();

            FileStream? output = null;
            try
            {
                if (destination != null) output = File.Create(destination);

                var lengthBuffer = new byte[4];
                long index = 0;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    if (input.Read(lengthBuffer, 0, 4) != 4) return BackupVerification.Truncated;
                    mac.TransformBlock(lengthBuffer, 0, 4, null, 0);

                    var length = BitConverter.ToInt32(lengthBuffer);
                    if (length == 0) break;
                    if (length < 0 || length > ChunkSize + 4096) return BackupVerification.Tampered;

                    var chunk = new byte[length];
                    if (input.Read(chunk, 0, length) != length) return BackupVerification.Truncated;
                    mac.TransformBlock(chunk, 0, length, null, 0);

                    var aad = new byte[16];
                    Buffer.BlockCopy(salt, 0, aad, 0, 8);
                    BitConverter.TryWriteBytes(aad.AsSpan(8), index);

                    if (!DataProtector.TryUnprotect(streamKey, chunk, aad, out var plain))
                        return BackupVerification.Tampered;

                    output?.Write(plain);
                    CryptographicOperations.ZeroMemory(plain);
                    index++;
                }

                mac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                var expected = mac.Hash!;
                var actual = new byte[expected.Length];
                if (input.Read(actual, 0, actual.Length) != actual.Length) return BackupVerification.Truncated;
                if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return BackupVerification.Tampered;
                if (input.ReadByte() != -1) return BackupVerification.Tampered;

                output?.Flush(true);
                return BackupVerification.Valid;
            }
            finally
            {
                output?.Dispose();
            }
        }
        catch (CryptographicException)
        {
            return BackupVerification.WrongKey;
        }
        catch (IOException)
        {
            return BackupVerification.Missing;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (streamKey != null) CryptographicOperations.ZeroMemory(streamKey);
            if (macKey != null) CryptographicOperations.ZeroMemory(macKey);
        }
    }

    public IReadOnlyList<BackupInfo> List()
    {
        _authz.Require(Permissions.Backup);
        return Enumerate();
    }

    IReadOnlyList<BackupInfo> Enumerate()
    {
        if (!Directory.Exists(_paths.BackupDirectory)) return Array.Empty<BackupInfo>();

        return Directory.EnumerateFiles(_paths.BackupDirectory, "b-*.gdb")
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.CreationTimeUtc)
            .Select(x => new BackupInfo(x.FullName, new DateTimeOffset(x.CreationTimeUtc, TimeSpan.Zero), x.Length))
            .ToList();
    }

    public int Prune(int keep)
    {
        if (keep < 1) keep = 1;

        int removed = 0;
        foreach (var stale in Enumerate().Skip(keep))
        {
            if (TryDelete(stale.Path)) removed++;
        }

        return removed;
    }

    static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
