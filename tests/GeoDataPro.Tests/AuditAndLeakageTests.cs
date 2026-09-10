using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Security;
using Xunit;

namespace GeoDataPro.Tests;

public class AuditAndLeakageTests
{
    [Fact]
    public async Task LoginOutcomesAreAudited()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();

        await host.LoginAsync("admin", "wrong-value-11!");
        await host.LoginAsync("admin", TestHost.AdminPassword);
        await host.Host.Authentication.LogoutAsync();

        await using var db = host.Host.Database.Create();
        var actions = db.AuditEntries.Select(x => x.Action).ToList();

        Assert.Contains(AuditActions.LoginFailed, actions);
        Assert.Contains(AuditActions.LoginSuccess, actions);
        Assert.Contains(AuditActions.Logout, actions);
        Assert.Contains(AuditActions.UserCreated, actions);
    }

    [Fact]
    public async Task DataOperationsAreAuditedWithActorAndEntity()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-18");
        var well = host.Host.Data.CreateWell(project.Id, "AUDIT-1");
        host.Host.Data.SaveJournal(well.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 1, CoreRecoveryM = 1, ZoneName = "1" },
        });
        host.Host.Data.SoftDeleteWell(well.Id);

        await using var db = host.Host.Database.Create();
        var entries = db.AuditEntries.OrderBy(x => x.Id).ToList();

        var deletion = entries.Last(x => x.Action == AuditActions.Delete);
        Assert.Equal("Well", deletion.Entity);
        Assert.Equal(well.Id.ToString(), deletion.EntityId);
        Assert.Equal("admin", deletion.Username);
        Assert.Equal(AuditResults.Success, deletion.Result);
        Assert.False(string.IsNullOrEmpty(deletion.SessionId));
        Assert.False(string.IsNullOrEmpty(deletion.DeviceId));
        Assert.Equal("test", deletion.AppVersion);
    }

    [Fact]
    public async Task AuditChainDetectsDeletionAndEdits()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-19");
        host.Host.Data.CreateProject("Loyiha-20");

        Assert.True(await host.Host.Audit.VerifyChainAsync());

        await using (var db = host.Host.Database.Create())
        {
            var target = db.AuditEntries.AsNoTracking().OrderBy(x => x.Id).Skip(1).First();
            var forged = target.Action == "TAMPERED" ? "ALTERED" : "TAMPERED";

            db.Database.ExecuteSqlRaw("UPDATE AuditEntries SET Action = {0} WHERE Id = {1}",
                forged, target.Id);

            Assert.Equal(forged,
                db.AuditEntries.AsNoTracking().Single(x => x.Id == target.Id).Action);
        }

        Assert.False(await host.Host.Audit.VerifyChainAsync());
    }

    [Fact]
    public async Task AuditChainDetectsRemovedEntry()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-21");
        host.Host.Data.CreateProject("Loyiha-22");

        await using (var db = host.Host.Database.Create())
        {
            var target = db.AuditEntries.OrderBy(x => x.Id).Skip(1).First();
            db.Database.ExecuteSqlRaw("DELETE FROM AuditEntries WHERE Id = {0}", target.Id);
        }

        Assert.False(await host.Host.Audit.VerifyChainAsync());
    }

    [Fact]
    public async Task AuditNeverStoresCredentials()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Sessions.Clear();
        await host.LoginAsync("admin", TestHost.AdminPassword);

        await host.Host.Audit.WriteAsync(new AuditRecord(
            AuditActions.SecurityEvent, AuditResults.Failure, "Probe", "1",
            "password=" + TestHost.AdminPassword));

        await using var db = host.Host.Database.Create();
        foreach (var entry in db.AuditEntries)
        {
            Assert.DoesNotContain(TestHost.AdminPassword, entry.Reason ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain("$argon2", entry.Reason ?? "", StringComparison.Ordinal);
        }

        var raw = File.ReadAllText(host.DatabaseFile.Replace(".db", ".db"), System.Text.Encoding.Latin1);
        Assert.DoesNotContain(TestHost.AdminPassword, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditIsReadableOnlyWithPermission()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        await host.SeedUserAsync("auditor", "Samarqand!2026#Aud", AppRole.Auditor);

        host.Host.Sessions.Clear();
        await host.LoginAsync("auditor", "Samarqand!2026#Aud");

        Assert.True(host.Host.Authorization.Has(Permissions.AuditRead));
        Assert.False(host.Host.Authorization.Has(Permissions.SampleWrite));
    }

    [Theory]
    [InlineData("password=Secret123!")]
    [InlineData("Parol: Secret123!")]
    [InlineData("Data Source=C:\\db\\geodata.db;Password=abc")]
    [InlineData("token=eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$c2FsdA$aGFzaA")]
    [InlineData("api_key: 12345")]
    public void SecretPatternsAreRedacted(string input)
    {
        var scrubbed = Redactor.Scrub(input);
        Assert.Contains("[redacted]", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret123!", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("c2FsdA", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("geodata.db", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Users\\geolog\\AppData\\Local\\GeoDataPro\\geodata.db")]
    [InlineData("/home/geolog/.local/share/GeoDataPro/geodata.db")]
    public void FileSystemPathsAreRedacted(string input) =>
        Assert.Contains("[redacted]", Redactor.Scrub(input), StringComparison.Ordinal);

    [Fact]
    public void OrdinaryTextSurvivesRedaction() =>
        Assert.Equal("Quduq 1001 uchun 42 qator saqlandi.",
            Redactor.Scrub("Quduq 1001 uchun 42 qator saqlandi."));

    [Fact]
    public void DiagnosticLogNeverWritesSecretsOrStackTraceAtProductionLevel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gdp-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var log = new RollingFileLog(dir, DiagnosticLevel.Warning);
            var exception = new InvalidOperationException(
                "Data Source=C:\\secret\\geodata.db;Password=hunter2");

            var reference = log.Write(DiagnosticLevel.Error, "password=hunter2", exception);

            Assert.Matches("^[0-9A-F]{8}$", reference);

            var content = File.ReadAllText(Path.Combine(dir, "diag.log"));
            Assert.DoesNotContain("hunter2", content, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\secret", content, StringComparison.Ordinal);
            Assert.Contains(reference, content, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ErrorPresenterGivesReferenceInsteadOfInternals()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gdp-err-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var log = new RollingFileLog(dir, DiagnosticLevel.Warning);
            var presenter = new ErrorPresenter(log);

            var report = presenter.Describe(
                new InvalidOperationException("SELECT PasswordHash FROM Users WHERE Id = 1"),
                "Amalni bajarib bo'lmadi.");

            Assert.Equal("Amalni bajarib bo'lmadi.", report.UserMessage);
            Assert.Matches("^[0-9A-F]{8}$", report.Reference);
            Assert.DoesNotContain("PasswordHash", report.UserMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LogRotationBoundsDiskUsage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gdp-rot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var log = new RollingFileLog(dir, DiagnosticLevel.Debug);
            var filler = new string('x', 1900);

            for (int i = 0; i < 3000; i++) log.Write(DiagnosticLevel.Error, filler);

            var files = Directory.GetFiles(dir);
            Assert.True(files.Length <= RollingFileLog.MaxArchives + 1);
            foreach (var file in files)
                Assert.True(new FileInfo(file).Length < RollingFileLog.MaxFileBytes * 2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SecretsNeverAppearInSourceOrConfiguration()
    {
        var root = FindRepositoryRoot();
        var suspicious = new[]
        {
            "Password=", "password=", "pwd=", "ApiKey", "api_key",
            "BEGIN RSA PRIVATE KEY", "BEGIN PRIVATE KEY",
        };

        var vocabularyFiles = new[] { "SecurityLog.cs", "AuditService.cs" };
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".json" or ".config" or ".xaml")) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (vocabularyFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal)) continue;

            foreach (var line in File.ReadLines(file))
            {
                foreach (var marker in suspicious)
                {
                    var index = line.IndexOf(marker, StringComparison.Ordinal);
                    if (index < 0) continue;

                    var tail = line[(index + marker.Length)..].Trim();
                    if (tail.Length == 0) continue;
                    if (tail.StartsWith("keyMaterial", StringComparison.Ordinal)) continue;
                    if (tail.StartsWith("{", StringComparison.Ordinal)) continue;
                    if (tail.StartsWith("\" +", StringComparison.Ordinal)) continue;

                    offenders.Add(Path.GetRelativePath(root, file) + " :: " + line.Trim());
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAppSettingsOrEnvFilesAreCommitted()
    {
        var root = FindRepositoryRoot();

        var leaked = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(x => x is not null)
            .Where(x => x!.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
                        x.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
                        x.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(leaked);
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GeoDataPro.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
