using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoDataPro.Core.Diagnostics;

public enum DiagnosticLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

public interface IDiagnosticLog
{
    DiagnosticLevel MinimumLevel { get; }
    string Write(DiagnosticLevel level, string message, Exception? exception = null);
}

public sealed record ErrorReport(string Reference, string UserMessage);

public static class ErrorReference
{
    public static string New() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToUpperInvariant();
}

public static class Redactor
{
    static readonly string[] Markers =
    {
        "password", "parol", "passwd", "pwd", "secret", "token", "apikey", "api_key",
        "authorization", "bearer", "connectionstring", "connection string",
        "data source", "sqlcipher", "pragma key", "private key", "begin rsa",
        "argon2", "hmac", "salt=", "api_key", "apikey",
    };

    static readonly Regex[] Patterns =
    {
        new(@"(?i)(password|pwd|parol|secret|token|key)\s*[:=]\s*[^\s;,)}\]]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)),
        new(@"(?i)data\s+source\s*=\s*[^;]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)),
        new(@"(?i)\b[a-z]:\\[^\s""']{2,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)),
        new(@"(?<=\s|^)/(?:home|Users|root|var|etc)/[^\s""']{2,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)),
        new(@"\$argon2[a-z]*\$[^\s]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)),
    };

    public static string Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var lower = value.ToLowerInvariant();
        foreach (var marker in Markers)
            if (lower.Contains(marker, StringComparison.Ordinal))
                return "[redacted]";

        var text = value;
        foreach (var pattern in Patterns)
        {
            try { text = pattern.Replace(text, "[redacted]"); }
            catch (RegexMatchTimeoutException) { return "[redacted]"; }
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(char.IsControl(c) && c != '\n' && c != '\t' ? ' ' : c);
        return builder.ToString();
    }

    public static bool LooksSensitive(string? value) =>
        !string.IsNullOrEmpty(value) && Scrub(value).Contains("[redacted]", StringComparison.Ordinal);
}

public sealed class RollingFileLog : IDiagnosticLog
{
    public const long MaxFileBytes = 2L * 1024 * 1024;
    public const int MaxArchives = 3;
    const int MaxMessageLength = 2000;

    readonly string _path;
    readonly object _gate = new();

    public RollingFileLog(string directory, DiagnosticLevel minimumLevel)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException(null, nameof(directory));
        Directory.CreateDirectory(directory);
        _path = Path.Combine(Path.GetFullPath(directory), "diag.log");
        MinimumLevel = minimumLevel;
    }

    public DiagnosticLevel MinimumLevel { get; }

    public string Write(DiagnosticLevel level, string message, Exception? exception = null)
    {
        var reference = ErrorReference.New();
        if (level < MinimumLevel) return reference;

        var line = new StringBuilder()
            .Append('[').Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).Append("] ")
            .Append(level.ToString().ToUpperInvariant()).Append(' ')
            .Append(reference).Append(' ')
            .Append(Truncate(Redactor.Scrub(message)))
            .ToString();

        if (exception != null)
        {
            line += Environment.NewLine + "  " + Truncate(Redactor.Scrub(Describe(exception)));
            if (MinimumLevel <= DiagnosticLevel.Debug)
                line += Environment.NewLine + "  " + Truncate(Redactor.Scrub(exception.StackTrace ?? string.Empty));
        }

        lock (_gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return reference;
    }

    static string Describe(Exception exception)
    {
        var parts = new List<string>();
        var current = exception;
        int depth = 0;
        while (current != null && depth < 5)
        {
            parts.Add(current.GetType().FullName ?? current.GetType().Name);
            current = current.InnerException;
            depth++;
        }

        return string.Join(" <- ", parts);
    }

    void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxFileBytes) return;

        for (int i = MaxArchives; i >= 1; i--)
        {
            var older = _path + "." + i.ToString(CultureInfo.InvariantCulture);
            if (!File.Exists(older)) continue;
            if (i == MaxArchives) { File.Delete(older); continue; }
            File.Move(older, _path + "." + (i + 1).ToString(CultureInfo.InvariantCulture), true);
        }

        File.Move(_path, _path + ".1", true);
    }

    static string Truncate(string value) =>
        value.Length <= MaxMessageLength ? value : value[..MaxMessageLength];
}

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static readonly NullDiagnosticLog Instance = new();
    public DiagnosticLevel MinimumLevel => DiagnosticLevel.Error;
    public string Write(DiagnosticLevel level, string message, Exception? exception = null) => ErrorReference.New();
}

public interface IErrorPresenter
{
    ErrorReport Describe(Exception exception, string fallbackMessage);
}

public sealed class ErrorPresenter : IErrorPresenter
{
    readonly IDiagnosticLog _log;

    public ErrorPresenter(IDiagnosticLog log) => _log = log ?? NullDiagnosticLog.Instance;

    public ErrorReport Describe(Exception exception, string fallbackMessage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var reference = _log.Write(DiagnosticLevel.Error, fallbackMessage, exception);
        return new ErrorReport(reference, fallbackMessage);
    }
}
