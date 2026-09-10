using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GeoDataPro.Core.Backup;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Security;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GeoDataPro.Tests;

public class StorageAndRecoveryTests
{
    [Fact]
    public async Task DatabaseFileIsEncryptedAtRest()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Maxfiy-Konchilik-Loyihasi");
        var well = host.Host.Data.CreateWell(project.Id, "SECRET-WELL-42");
        host.Host.Data.SaveJournal(well.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 10, CoreRecoveryM = 9, ZoneName = "1", Description = "OLTIN-KONI-ANIQLANDI" },
        });

        SqliteConnection.ClearAllPools();
        var raw = File.ReadAllBytes(host.DatabaseFile);

        Assert.False(raw.Take(16).SequenceEqual(Encoding.ASCII.GetBytes("SQLite format 3\0")));

        var text = Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("Maxfiy-Konchilik-Loyihasi", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-WELL-42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OLTIN-KONI-ANIQLANDI", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StolenDatabaseFileCannotBeOpenedWithoutTheKey()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-09");

        SqliteConnection.ClearAllPools();
        var copy = Path.Combine(host.Root, "stolen.db");
        File.Copy(host.DatabaseFile, copy);

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString());

        var ex = Record.Exception(() =>
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Projects";
            command.ExecuteScalar();
        });

        Assert.NotNull(ex);

        using var wrongKey = new SqliteConnection(
            DatabaseLocation.BuildConnectionString(copy, "00000000000000000000000000000000"));
        Assert.NotNull(Record.Exception(() => wrongKey.Open()));
    }

    [Fact]
    public void LegacyPlaintextDatabaseIsConvertedWithoutDataLoss()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gdp-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var file = Path.Combine(dir, "geodata.db");
            using (var plain = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
            {
                plain.Open();
                using var create = plain.CreateCommand();
                create.CommandText = "CREATE TABLE Legacy(Id INTEGER PRIMARY KEY, Payload TEXT); " +
                                     "INSERT INTO Legacy(Payload) VALUES('kern-namunasi-2024');";
                create.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            Assert.Equal(ProtectionState.Unprotected, DatabaseProtection.Inspect(file));

            const string key = "0123456789abcdef0123456789abcdef";
            var outcome = DatabaseProtection.Protect(file, key, out var rescue);

            Assert.Equal(ProtectionOutcome.Converted, outcome);
            Assert.Equal(ProtectionState.Protected, DatabaseProtection.Inspect(file));
            Assert.NotNull(rescue);
            Assert.True(File.Exists(rescue));

            using var encrypted = new SqliteConnection(DatabaseLocation.BuildConnectionString(file, key));
            encrypted.Open();
            using var read = encrypted.CreateCommand();
            read.CommandText = "SELECT Payload FROM Legacy";
            Assert.Equal("kern-namunasi-2024", read.ExecuteScalar());

            Assert.Equal(ProtectionOutcome.NothingToDo, DatabaseProtection.Protect(file, key, out _));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BackupIsEncryptedVerifiableAndRestorable()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Zaxira-Loyiha");
        var well = host.Host.Data.CreateWell(project.Id, "BACKUP-WELL-7");

        var info = await host.Host.Backup.CreateAsync();

        Assert.True(File.Exists(info.Path));
        var bytes = File.ReadAllBytes(info.Path);
        Assert.Equal("GDPB", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.DoesNotContain("BACKUP-WELL-7", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("Zaxira-Loyiha", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        Assert.Equal(BackupVerification.Valid, await host.Host.Backup.VerifyAsync(info.Path));

        host.Host.Data.SoftDeleteWell(well.Id);
        Assert.Empty(host.Host.Data.GetWells(project.Id));

        SqliteConnection.ClearAllPools();
        await host.Host.Backup.RestoreAsync(info.Path);
        SqliteConnection.ClearAllPools();

        Assert.Single(host.Host.Data.GetWells(project.Id));
    }

    [Fact]
    public async Task TamperedBackupIsDetectedAndNotRestored()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-10");

        var info = await host.Host.Backup.CreateAsync();

        var bytes = File.ReadAllBytes(info.Path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(info.Path, bytes);

        Assert.NotEqual(BackupVerification.Valid, await host.Host.Backup.VerifyAsync(info.Path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Host.Backup.RestoreAsync(info.Path));
    }

    [Fact]
    public async Task TruncatedAndForeignBackupsAreRejected()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-11");

        var info = await host.Host.Backup.CreateAsync();

        var truncated = Path.Combine(host.Root, "cut.gdb");
        var bytes = File.ReadAllBytes(info.Path);
        File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray());
        Assert.NotEqual(BackupVerification.Valid, await host.Host.Backup.VerifyAsync(truncated));

        var foreign = Path.Combine(host.Root, "foreign.gdb");
        File.WriteAllBytes(foreign, Encoding.ASCII.GetBytes("NOTAGDPBACKUPFILE-000000000000000000"));
        Assert.Equal(BackupVerification.BadMagic, await host.Host.Backup.VerifyAsync(foreign));

        Assert.Equal(BackupVerification.Missing,
            await host.Host.Backup.VerifyAsync(Path.Combine(host.Root, "absent.gdb")));
    }

    [Fact]
    public async Task BackupRotationKeepsBoundedHistory()
    {
        var clock = new MutableClock();
        using var host = TestHost.Create(clock);
        await host.SeedAdminAsync();
        host.Host.Data.CreateProject("Loyiha-12");

        for (int i = 0; i < 4; i++)
        {
            await host.Host.Backup.CreateAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(4, host.Host.Backup.List().Count);

        host.Host.Backup.Prune(2);
        Assert.Equal(2, host.Host.Backup.List().Count);
    }

    [Fact]
    public async Task TamperedRowIsDetectedOnNextWrite()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-13");
        var well = host.Host.Data.CreateWell(project.Id, "TAMPER-1");
        host.Host.Data.SaveJournal(well.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 10, CoreRecoveryM = 8, ZoneName = "1", Description = "asl yozuv" },
        });

        await using (var db = host.Host.Database.Create())
        {
            db.Database.ExecuteSqlRaw(
                "UPDATE JournalRows SET Description = {0} WHERE WellId = {1}",
                "soxta yozuv", well.Id);
        }

        var rows = host.Host.Data.GetJournalRows(well.Id).ToList();
        Assert.Equal("soxta yozuv", rows[0].Description);

        Assert.Throws<IntegrityViolationException>(() => host.Host.Data.SaveJournal(well.Id, rows));
    }

    [Fact]
    public async Task TamperedWellIsDetectedOnUpdate()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-14");
        var well = host.Host.Data.CreateWell(project.Id, "TAMPER-2");

        await using (var db = host.Host.Database.Create())
            db.Database.ExecuteSqlRaw("UPDATE Wells SET Geologist = {0} WHERE Id = {1}", "soxta", well.Id);

        var edited = host.Host.Data.GetWells(project.Id).Single();
        edited.Geologist = "haqiqiy";

        Assert.Throws<IntegrityViolationException>(() => host.Host.Data.UpdateWell(edited));
    }

    [Fact]
    public async Task SoftDeleteHidesDataButKeepsItRestorable()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-15");
        var well = host.Host.Data.CreateWell(project.Id, "RESTORE-1");
        host.Host.Data.SaveJournal(well.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 3, CoreRecoveryM = 3, ZoneName = "1" },
        });

        host.Host.Data.SoftDeleteWell(well.Id);

        Assert.Empty(host.Host.Data.GetWells(project.Id));
        Assert.Empty(host.Host.Data.GetJournalRows(well.Id));

        await using (var db = host.Host.Database.Create())
        {
            Assert.True(db.Wells.Single(x => x.Id == well.Id).IsDeleted);
            Assert.True(db.JournalRows.Single(x => x.WellId == well.Id).IsDeleted);
        }

        host.Host.Data.RestoreWell(well.Id);

        Assert.Single(host.Host.Data.GetWells(project.Id));
        Assert.Single(host.Host.Data.GetJournalRows(well.Id));
    }

    [Fact]
    public async Task PurgeArchivesRecordBeforeRemoval()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-16");
        var well = host.Host.Data.CreateWell(project.Id, "PURGE-1");
        host.Host.Data.SaveJournal(well.Id, new List<JournalRow>
        {
            new() { Top = 0, Bottom = 2, CoreRecoveryM = 2, ZoneName = "1", Description = "arxivlanadi" },
        });

        host.Host.Data.PurgeWell(well.Id);

        await using var db = host.Host.Database.Create();
        Assert.False(db.Wells.Any(x => x.Id == well.Id));

        var archived = db.DeletedRecords.Single(x => x.Entity == "Well");
        Assert.Contains("arxivlanadi", archived.Payload, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(archived.Stamp));
    }

    [Fact]
    public async Task DatabaseIntegrityChecksPass()
    {
        using var host = TestHost.Create();
        await host.SeedAdminAsync();

        var project = host.Host.Data.CreateProject("Loyiha-17");
        host.Host.Data.CreateWell(project.Id, "CHECK-1");

        await using var db = host.Host.Database.Create();
        Assert.True(db.QuickCheck());
        Assert.True(db.ForeignKeyCheck());
    }
}
