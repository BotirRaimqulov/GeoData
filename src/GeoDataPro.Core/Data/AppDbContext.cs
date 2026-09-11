using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeoDataPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Well> Wells => Set<Well>();
    public DbSet<LithoCode> LithoCodes => Set<LithoCode>();
    public DbSet<ColorCode> ColorCodes => Set<ColorCode>();
    public DbSet<TextureCode> TextureCodes => Set<TextureCode>();
    public DbSet<MineralCode> MineralCodes => Set<MineralCode>();
    public DbSet<FloraFaunaCode> FloraFaunaCodes => Set<FloraFaunaCode>();
    public DbSet<IronHydroxideCode> IronHydroxideCodes => Set<IronHydroxideCode>();
    public DbSet<ClasticMaterialCode> ClasticMaterialCodes => Set<ClasticMaterialCode>();
    public DbSet<DescriptionTemplate> DescriptionTemplates => Set<DescriptionTemplate>();
    public DbSet<JournalRow> JournalRows => Set<JournalRow>();
    public DbSet<SampleRow> SampleRows => Set<SampleRow>();
    public DbSet<SrpRow> SrpRows => Set<SrpRow>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SecurityFlag> SecurityFlags => Set<SecurityFlag>();
    public DbSet<DeletedRecord> DeletedRecords => Set<DeletedRecord>();

    readonly string? _connectionString;

    public AppDbContext() => _connectionString = null;

    public AppDbContext(string connectionString) =>
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) => _connectionString = null;

    public static string DbPath => DatabaseLocation.DatabaseFile;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (options.IsConfigured) return;
        options.UseSqlite(_connectionString ?? DatabaseLocation.BuildConnectionString());
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<JournalRow>().Ignore(x => x.Interval).Ignore(x => x.RecoveryPercent);
        b.Entity<SampleRow>().Ignore(x => x.Length);

        b.Entity<LithoCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ColorCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<TextureCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<MineralCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<FloraFaunaCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<IronHydroxideCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ClasticMaterialCode>().HasIndex(x => x.Code).IsUnique();

        b.Entity<Project>().Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Entity<Project>().HasIndex(x => x.Name);
        b.Entity<Project>().Property(x => x.RowVersion).IsConcurrencyToken();

        b.Entity<Well>().Property(x => x.Number).IsRequired().HasMaxLength(100);
        b.Entity<Well>().HasIndex(x => new { x.ProjectId, x.Number });
        b.Entity<Well>().Property(x => x.RowVersion).IsConcurrencyToken();
        b.Entity<Well>()
            .HasOne(x => x.Project)
            .WithMany(x => x.Wells)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<JournalRow>().HasIndex(x => new { x.WellId, x.OrderNo });
        b.Entity<JournalRow>().Property(x => x.RowVersion).IsConcurrencyToken();
        b.Entity<JournalRow>()
            .HasOne(x => x.Well)
            .WithMany(x => x.JournalRows)
            .HasForeignKey(x => x.WellId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalRow>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_JournalRows_Depth", "\"Bottom\" >= \"Top\"");
            t.HasCheckConstraint("CK_JournalRows_Core", "\"CoreRecoveryM\" >= 0");
        });

        b.Entity<SampleRow>().HasIndex(x => new { x.WellId, x.SampleNumber });
        b.Entity<SampleRow>().Property(x => x.SampleNumber).IsRequired().HasMaxLength(100);
        b.Entity<SampleRow>().Property(x => x.RowVersion).IsConcurrencyToken();
        b.Entity<SampleRow>()
            .HasOne(x => x.Well)
            .WithMany(x => x.SampleRows)
            .HasForeignKey(x => x.WellId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<SampleRow>().ToTable(t => t.HasCheckConstraint("CK_SampleRows_Depth", "\"Bottom\" >= \"Top\""));

        b.Entity<SrpRow>().HasIndex(x => new { x.WellId, x.Md });
        b.Entity<SrpRow>().Property(x => x.RowVersion).IsConcurrencyToken();
        b.Entity<SrpRow>()
            .HasOne(x => x.Well)
            .WithMany(x => x.SrpRows)
            .HasForeignKey(x => x.WellId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<AppUser>().HasIndex(x => x.UsernameNormalized).IsUnique();
        b.Entity<AppUser>().Property(x => x.Username).IsRequired().HasMaxLength(64);
        b.Entity<AppUser>().Property(x => x.UsernameNormalized).IsRequired().HasMaxLength(64);
        b.Entity<AppUser>().Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
        b.Entity<AppUser>().Property(x => x.RowVersion).IsConcurrencyToken();
        b.Entity<AppUser>().ToTable(t => t.HasCheckConstraint("CK_Users_Role", "\"Role\" BETWEEN 0 AND 4"));

        b.Entity<AuditEntry>().HasIndex(x => x.TimestampUtc);
        b.Entity<AuditEntry>().HasIndex(x => x.Action);
        b.Entity<AuditEntry>().Property(x => x.Action).IsRequired().HasMaxLength(64);
        b.Entity<AuditEntry>().Property(x => x.Result).IsRequired().HasMaxLength(32);

        b.Entity<SecurityFlag>().HasIndex(x => x.Name).IsUnique();
        b.Entity<SecurityFlag>().Property(x => x.Name).IsRequired().HasMaxLength(64);

        b.Entity<DeletedRecord>().HasIndex(x => new { x.Entity, x.EntityId });
        b.Entity<DeletedRecord>().Property(x => x.Entity).IsRequired().HasMaxLength(64);
        b.Entity<DeletedRecord>().Property(x => x.EntityId).IsRequired().HasMaxLength(64);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override System.Threading.Tasks.Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        System.Threading.CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries<ITrackedEntity>())
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion = Guid.NewGuid().ToString("N");

        foreach (var entry in ChangeTracker.Entries<AppUser>())
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion = Guid.NewGuid().ToString("N");
    }

    // Increment when adding new migrations or reseed logic.
    const int CurrentStartupVersion = 2;

    public void EnsureSeeded()
    {
        Database.EnsureCreated();
        ApplySafetyPragmas();

        if (!IsStartupVersionCurrent())
        {
            ApplyLightMigrations();
            Seed.Run(this);
            SaveStartupVersion();
        }
    }

    bool IsStartupVersionCurrent()
    {
        try
        {
            var tableExists = Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type='table' AND name='SecurityFlags'")
                .AsEnumerable().FirstOrDefault() != 0;
            if (!tableExists) return false;
            var ver = Database.SqlQueryRaw<string>(
                "SELECT COALESCE(Value,'0') AS \"Value\" FROM SecurityFlags WHERE Name='startup_version'")
                .AsEnumerable().FirstOrDefault();
            return ver == CurrentStartupVersion.ToString();
        }
        catch { return false; }
    }

#pragma warning disable EF1002
    void SaveStartupVersion()
    {
        try
        {
            Database.ExecuteSqlRaw(
                "INSERT INTO SecurityFlags(Name,Value,UpdatedUtc) " +
                $"VALUES('startup_version','{CurrentStartupVersion}','{DateTime.UtcNow:O}') " +
                "ON CONFLICT(Name) DO UPDATE SET Value=excluded.Value,UpdatedUtc=excluded.UpdatedUtc");
        }
        catch { }
    }
#pragma warning restore EF1002

    void ApplySafetyPragmas()
    {
        Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
        Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL");
        Database.ExecuteSqlRaw("PRAGMA synchronous = FULL");
        Database.ExecuteSqlRaw("PRAGMA secure_delete = ON");
        Database.ExecuteSqlRaw("PRAGMA temp_store = MEMORY");
        Database.ExecuteSqlRaw("PRAGMA trusted_schema = OFF");
    }

    public bool QuickCheck()
    {
        var result = Database.SqlQueryRaw<string>("PRAGMA quick_check").AsEnumerable().FirstOrDefault();
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    public bool ForeignKeyCheck() =>
        Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_foreign_key_check")
            .AsEnumerable().FirstOrDefault() == 0;

#pragma warning disable EF1002
    void ApplyLightMigrations()
    {
        bool ColumnExists(string table, string column)
        {
            var sql = "SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({0}) WHERE name = {1}";
            return Database.SqlQueryRaw<int>(sql, table, column).AsEnumerable().FirstOrDefault() != 0;
        }

        bool TableExists(string table)
        {
            const string sql = "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}";
            return Database.SqlQueryRaw<int>(sql, table).AsEnumerable().FirstOrDefault() != 0;
        }

        void AddColumn(string table, string column, string decl)
        {
            var safeTable = SqlIdentifier.QuoteFrom(table, KnownTables);
            var safeColumn = SqlIdentifier.Quote(column);
            var safeDecl = SqlIdentifier.ColumnType(decl);
            if (ColumnExists(table, column)) return;
            Database.ExecuteSqlRaw($"ALTER TABLE {safeTable} ADD COLUMN {safeColumn} {safeDecl}");
        }

        void RemoveColumn(string table, string column)
        {
            var safeTable = SqlIdentifier.QuoteFrom(table, KnownTables);
            var safeColumn = SqlIdentifier.Quote(column);
            if (!ColumnExists(table, column)) return;
            Database.ExecuteSqlRaw($"ALTER TABLE {safeTable} DROP COLUMN {safeColumn}");
        }

        void EnsureRefTable(string table)
        {
            var safeTable = SqlIdentifier.QuoteFrom(table, KnownTables);
            if (TableExists(table)) return;
            Database.ExecuteSqlRaw(
                $"CREATE TABLE {safeTable} (" +
                $"\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_{table}\" PRIMARY KEY AUTOINCREMENT, " +
                "\"Code\" INTEGER NOT NULL, " +
                "\"Name\" TEXT NOT NULL, " +
                "\"NameRu\" TEXT NULL, " +
                "\"PatternKey\" TEXT NULL)");
            Database.ExecuteSqlRaw($"CREATE UNIQUE INDEX \"IX_{table}_Code\" ON {safeTable} (\"Code\")");
        }

        void EnsureSecurityTables()
        {
            if (!TableExists("Users"))
            {
                Database.ExecuteSqlRaw(
                    "CREATE TABLE \"Users\" (" +
                    "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Users\" PRIMARY KEY AUTOINCREMENT, " +
                    "\"Username\" TEXT NOT NULL, " +
                    "\"UsernameNormalized\" TEXT NOT NULL, " +
                    "\"DisplayName\" TEXT NOT NULL, " +
                    "\"PasswordHash\" TEXT NOT NULL, " +
                    "\"Role\" INTEGER NOT NULL, " +
                    "\"IsActive\" INTEGER NOT NULL, " +
                    "\"MustChangePassword\" INTEGER NOT NULL, " +
                    "\"FailedAttempts\" INTEGER NOT NULL, " +
                    "\"LockoutEndUtc\" TEXT NULL, " +
                    "\"LastLoginUtc\" TEXT NULL, " +
                    "\"CreatedUtc\" TEXT NOT NULL, " +
                    "\"UpdatedUtc\" TEXT NOT NULL, " +
                    "\"RowVersion\" TEXT NOT NULL, " +
                    "\"Stamp\" TEXT NULL, " +
                    "CONSTRAINT \"CK_Users_Role\" CHECK (\"Role\" BETWEEN 0 AND 4))");
                Database.ExecuteSqlRaw(
                    "CREATE UNIQUE INDEX \"IX_Users_UsernameNormalized\" ON \"Users\" (\"UsernameNormalized\")");
            }

            if (!TableExists("AuditEntries"))
            {
                Database.ExecuteSqlRaw(
                    "CREATE TABLE \"AuditEntries\" (" +
                    "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_AuditEntries\" PRIMARY KEY AUTOINCREMENT, " +
                    "\"TimestampUtc\" TEXT NOT NULL, " +
                    "\"UserId\" INTEGER NULL, " +
                    "\"Username\" TEXT NULL, " +
                    "\"Action\" TEXT NOT NULL, " +
                    "\"Entity\" TEXT NULL, " +
                    "\"EntityId\" TEXT NULL, " +
                    "\"Result\" TEXT NOT NULL, " +
                    "\"Reason\" TEXT NULL, " +
                    "\"AppVersion\" TEXT NULL, " +
                    "\"DeviceId\" TEXT NULL, " +
                    "\"SessionId\" TEXT NULL, " +
                    "\"PrevChain\" TEXT NULL, " +
                    "\"Chain\" TEXT NULL)");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX \"IX_AuditEntries_TimestampUtc\" ON \"AuditEntries\" (\"TimestampUtc\")");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX \"IX_AuditEntries_Action\" ON \"AuditEntries\" (\"Action\")");
            }

            if (!TableExists("SecurityFlags"))
            {
                Database.ExecuteSqlRaw(
                    "CREATE TABLE \"SecurityFlags\" (" +
                    "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_SecurityFlags\" PRIMARY KEY AUTOINCREMENT, " +
                    "\"Name\" TEXT NOT NULL, " +
                    "\"Value\" TEXT NULL, " +
                    "\"UpdatedUtc\" TEXT NOT NULL)");
                Database.ExecuteSqlRaw(
                    "CREATE UNIQUE INDEX \"IX_SecurityFlags_Name\" ON \"SecurityFlags\" (\"Name\")");
            }

            if (!TableExists("DeletedRecords"))
            {
                Database.ExecuteSqlRaw(
                    "CREATE TABLE \"DeletedRecords\" (" +
                    "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_DeletedRecords\" PRIMARY KEY AUTOINCREMENT, " +
                    "\"Entity\" TEXT NOT NULL, " +
                    "\"EntityId\" TEXT NOT NULL, " +
                    "\"Payload\" TEXT NOT NULL, " +
                    "\"DeletedUtc\" TEXT NOT NULL, " +
                    "\"DeletedByUserId\" INTEGER NULL, " +
                    "\"Stamp\" TEXT NULL, " +
                    "\"Restored\" INTEGER NOT NULL)");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX \"IX_DeletedRecords_Entity_EntityId\" ON \"DeletedRecords\" (\"Entity\", \"EntityId\")");
            }
        }

        using var tx = Database.BeginTransaction();

        EnsureSecurityTables();

        EnsureRefTable("FloraFaunaCodes");
        EnsureRefTable("IronHydroxideCodes");
        EnsureRefTable("ClasticMaterialCodes");

        AddColumn("LithoCodes", "NameRu", "TEXT NULL");
        AddColumn("ColorCodes", "NameRu", "TEXT NULL");
        AddColumn("TextureCodes", "NameRu", "TEXT NULL");
        AddColumn("MineralCodes", "NameRu", "TEXT NULL");

        AddColumn("JournalRows", "MineralCode", "INTEGER NULL");
        AddColumn("JournalRows", "GrainSize", "TEXT NULL");
        AddColumn("JournalRows", "Hardness", "TEXT NULL");
        AddColumn("JournalRows", "Cementation", "TEXT NULL");
        AddColumn("JournalRows", "FloraFaunaCode", "INTEGER NULL");
        AddColumn("JournalRows", "IronHydroxideCode", "INTEGER NULL");
        AddColumn("JournalRows", "Composition", "TEXT NULL");
        AddColumn("JournalRows", "ClasticMaterialCode", "INTEGER NULL");
        AddColumn("JournalRows", "ClasticMaterialCodes", "TEXT NULL");

        Database.ExecuteSqlRaw(
            "UPDATE JournalRows SET ClasticMaterialCodes = CAST(ClasticMaterialCode AS TEXT) " +
            "WHERE ClasticMaterialCode IS NOT NULL AND (ClasticMaterialCodes IS NULL OR ClasticMaterialCodes = '')");

        RemoveColumn("JournalRows", "CarbonateCo2");

        AddColumn("DescriptionTemplates", "LithoCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "ColorCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "TextureCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "MineralCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "GrainSize", "TEXT NULL");
        AddColumn("SampleRows", "SampleTypeCode", "INTEGER NULL");

        foreach (var table in TrackedTables)
        {
            var quoted = SqlIdentifier.QuoteFrom(table, KnownTables);
            AddColumn(table, "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
            AddColumn(table, "DeletedUtc", "TEXT NULL");
            AddColumn(table, "DeletedByUserId", "INTEGER NULL");
            AddColumn(table, "RowVersion", "TEXT NOT NULL DEFAULT ''");
            AddColumn(table, "Stamp", "TEXT NULL");
            Database.ExecuteSqlRaw(
                $"UPDATE {quoted} SET \"RowVersion\" = lower(hex(randomblob(16))) " +
                "WHERE \"RowVersion\" IS NULL OR \"RowVersion\" = ''");
        }

        tx.Commit();
    }

#pragma warning restore EF1002

    static readonly IReadOnlyCollection<string> TrackedTables = new[]
    {
        "Projects", "Wells", "JournalRows", "SampleRows", "SrpRows",
    };

    static readonly IReadOnlyCollection<string> KnownTables = new[]
    {
        "Projects", "Wells", "JournalRows", "SampleRows", "SrpRows",
        "LithoCodes", "ColorCodes", "TextureCodes", "MineralCodes",
        "FloraFaunaCodes", "IronHydroxideCodes", "ClasticMaterialCodes",
        "DescriptionTemplates", "Users", "AuditEntries", "SecurityFlags", "DeletedRecords",
    };
}
