using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GeoDataPro.Core.Files;

public enum FileRejection
{
    None = 0,
    Missing,
    Empty,
    TooLarge,
    BadExtension,
    BadSignature,
    Traversal,
    OutsideRoot,
    ArchiveTooLarge,
    ArchiveTooManyEntries,
    ArchiveUnsafeEntry,
    NotReadable,
}

public sealed class FileGuardException : Exception
{
    public FileGuardException(FileRejection reason) : base("E_FILE") => Reason = reason;
    public FileRejection Reason { get; }
}

public static class SafeFile
{
    public const long MaxWorkbookBytes = 128L * 1024 * 1024;
    public const long MaxArchiveExpandedBytes = 1024L * 1024 * 1024;
    public const int MaxArchiveEntries = 4096;
    const int MaxPathLength = 32000;

    static readonly char[] IllegalNameChars =
        Path.GetInvalidFileNameChars()
            .Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' })
            .Distinct()
            .ToArray();

    public static bool HasTraversalSegment(string candidate)
    {
        foreach (var segment in candidate.Split('/', '\\'))
            if (segment == ".." || segment == "...")
                return true;
        return false;
    }

    public static string EnsureUnder(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException(null, nameof(root));
        if (string.IsNullOrWhiteSpace(candidate)) throw new FileGuardException(FileRejection.Traversal);
        if (candidate.Length > MaxPathLength) throw new FileGuardException(FileRejection.Traversal);
        if (candidate.IndexOf('\0') >= 0) throw new FileGuardException(FileRejection.Traversal);
        if (HasTraversalSegment(candidate)) throw new FileGuardException(FileRejection.Traversal);

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var combined = Path.IsPathRooted(candidate) ? candidate : Path.Combine(fullRoot, candidate);
        var full = Path.GetFullPath(combined);

        if (!full.Equals(fullRoot, StringComparison.Ordinal) &&
            !full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FileGuardException(FileRejection.OutsideRoot);

        return full;
    }

    public static string SanitizeFileName(string? name, string fallback = "export")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var trimmed = name.Trim();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            builder.Append(char.IsControl(c) || IllegalNameChars.Contains(c) ? '_' : c);

        var result = builder.ToString().Trim('.', ' ');
        if (result.Length == 0) return fallback;
        if (result.Length > 120) result = result[..120];

        var stem = Path.GetFileNameWithoutExtension(result);
        if (IsReservedDeviceName(stem)) result = "_" + result;

        return result;
    }

    static bool IsReservedDeviceName(string stem)
    {
        string[] reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };
        return reserved.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    public static void ValidateWorkbook(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FileGuardException(FileRejection.Missing);
        if (path.IndexOf('\0') >= 0) throw new FileGuardException(FileRejection.Traversal);

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { throw new FileGuardException(FileRejection.Traversal); }

        if (!string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new FileGuardException(FileRejection.BadExtension);

        FileInfo info;
        try { info = new FileInfo(full); }
        catch (Exception) { throw new FileGuardException(FileRejection.NotReadable); }

        if (!info.Exists) throw new FileGuardException(FileRejection.Missing);
        if (info.Length == 0) throw new FileGuardException(FileRejection.Empty);
        if (info.Length > MaxWorkbookBytes) throw new FileGuardException(FileRejection.TooLarge);

        ValidateZipSignature(full);
        ValidateArchive(full);
    }

    static void ValidateZipSignature(string path)
    {
        Span<byte> header = stackalloc byte[4];
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Read(header) != 4) throw new FileGuardException(FileRejection.BadSignature);
        }
        catch (IOException)
        {
            throw new FileGuardException(FileRejection.NotReadable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new FileGuardException(FileRejection.NotReadable);
        }

        if (header[0] != 0x50 || header[1] != 0x4B || header[2] != 0x03 || header[3] != 0x04)
            throw new FileGuardException(FileRejection.BadSignature);
    }

    static void ValidateArchive(string path)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new FileGuardException(FileRejection.BadSignature);
        }
        catch (IOException)
        {
            throw new FileGuardException(FileRejection.NotReadable);
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxArchiveEntries)
                throw new FileGuardException(FileRejection.ArchiveTooManyEntries);

            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.Length > 1024) throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);
                if (name.IndexOf('\0') >= 0) throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);
                if (name.StartsWith('/') || name.StartsWith('\\')) throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);
                if (name.Contains("..", StringComparison.Ordinal)) throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);
                if (name.Length > 1 && name[1] == ':') throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);

                if (entry.Length < 0) throw new FileGuardException(FileRejection.ArchiveUnsafeEntry);
                expanded += entry.Length;
                if (expanded > MaxArchiveExpandedBytes)
                    throw new FileGuardException(FileRejection.ArchiveTooLarge);
            }
        }
    }

    public static void WriteAtomic(string destination, Action<string> writeToTemp)
    {
        ArgumentNullException.ThrowIfNull(writeToTemp);
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException(null, nameof(destination));

        var full = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory)) throw new FileGuardException(FileRejection.OutsideRoot);
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            writeToTemp(temp);
            if (File.Exists(full)) File.Replace(temp, full, null);
            else File.Move(temp, full);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
