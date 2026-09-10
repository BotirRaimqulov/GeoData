using System;
using System.Globalization;
using System.IO;
using GeoDataPro.Core.Security;
using Microsoft.Data.Sqlite;

namespace GeoDataPro.Core.Data;

public enum ProtectionState
{
    Absent = 0,
    Protected,
    Unprotected,
    Unreadable,
}

public enum ProtectionOutcome
{
    NothingToDo = 0,
    Converted,
    Adopted,
    Failed,
}

public static class DatabaseProtection
{
    public static ProtectionState Inspect(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return ProtectionState.Absent;

        try
        {
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[16];
            if (stream.Read(header) < 16) return ProtectionState.Unreadable;
            return header.SequenceEqual("SQLite format 3\0"u8)
                ? ProtectionState.Unprotected
                : ProtectionState.Protected;
        }
        catch (IOException)
        {
            return ProtectionState.Unreadable;
        }
        catch (UnauthorizedAccessException)
        {
            return ProtectionState.Unreadable;
        }
    }

    public static ProtectionOutcome Protect(string file, string keyMaterial, out string? rescueCopy)
    {
        rescueCopy = null;
        if (string.IsNullOrWhiteSpace(keyMaterial)) throw new ArgumentException(null, nameof(keyMaterial));

        var state = Inspect(file);
        if (state is ProtectionState.Absent or ProtectionState.Protected) return ProtectionOutcome.NothingToDo;
        if (state == ProtectionState.Unreadable) return ProtectionOutcome.Failed;

        var directory = Path.GetDirectoryName(Path.GetFullPath(file))
            ?? throw new InvalidOperationException("E_PATH");
        var staging = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".conv");

        try
        {
            using (var source = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = file,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString()))
            {
                source.Open();

                using (var attach = source.CreateCommand())
                {
                    attach.CommandText = "ATTACH DATABASE $path AS enc KEY $key";
                    attach.Parameters.AddWithValue("$path", staging);
                    attach.Parameters.AddWithValue("$key", keyMaterial);
                    attach.ExecuteNonQuery();
                }

                using (var export = source.CreateCommand())
                {
                    export.CommandText = "SELECT sqlcipher_export('enc')";
                    export.ExecuteNonQuery();
                }

                using (var detach = source.CreateCommand())
                {
                    detach.CommandText = "DETACH DATABASE enc";
                    detach.ExecuteNonQuery();
                }
            }

            SqliteConnection.ClearAllPools();

            if (Inspect(staging) != ProtectionState.Protected) return ProtectionOutcome.Failed;

            using (var probe = new SqliteConnection(DatabaseLocation.BuildConnectionString(staging, keyMaterial)))
            {
                probe.Open();
                using var check = probe.CreateCommand();
                check.CommandText = "PRAGMA quick_check";
                if (check.ExecuteScalar() as string is not "ok") return ProtectionOutcome.Failed;
            }

            SqliteConnection.ClearAllPools();

            rescueCopy = file + ".plain-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
            File.Move(file, rescueCopy);
            File.Move(staging, file);

            Discard(file + "-wal");
            Discard(file + "-shm");
            Discard(rescueCopy + "-wal");
            Discard(rescueCopy + "-shm");

            return ProtectionOutcome.Converted;
        }
        catch (SqliteException)
        {
            Discard(staging);
            return ProtectionOutcome.Failed;
        }
        catch (IOException)
        {
            Discard(staging);
            return ProtectionOutcome.Failed;
        }
        finally
        {
            if (File.Exists(staging)) Discard(staging);
        }
    }

    public static ProtectionOutcome Adopt(string legacyFile, string targetFile, string keyMaterial)
    {
        if (!File.Exists(legacyFile)) return ProtectionOutcome.NothingToDo;
        if (File.Exists(targetFile)) return ProtectionOutcome.NothingToDo;
        if (string.Equals(Path.GetFullPath(legacyFile), Path.GetFullPath(targetFile), StringComparison.Ordinal))
            return ProtectionOutcome.NothingToDo;

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetFile));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        try
        {
            File.Copy(legacyFile, targetFile, false);
        }
        catch (IOException)
        {
            return ProtectionOutcome.Failed;
        }

        var outcome = Protect(targetFile, keyMaterial, out _);
        return outcome == ProtectionOutcome.Failed ? ProtectionOutcome.Failed : ProtectionOutcome.Adopted;
    }

    static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
