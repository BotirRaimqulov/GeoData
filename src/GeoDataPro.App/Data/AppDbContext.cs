using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.App.Data;

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

    public static string DbPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeoDataPro");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "geodata.db");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

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
    }

    /// <summary>Bazani yaratadi va spravochniklarni seed qiladi.</summary>
    public void EnsureSeeded()
    {
        Database.EnsureCreated();
        ApplyLightMigrations();
        Seed.Run(this);
    }

    /// <summary>
    /// EnsureCreated() eski bazalarga yangi ustunlarni qo'shmaydi — shu sabab
    /// kerakli "ADD COLUMN" larni qo'lda, xavfsiz tarzda bajaramiz.
    /// </summary>
    void ApplyLightMigrations()
    {
        void AddColumn(string table, string column, string decl)
        {
            var existsSql = $"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{table}') WHERE name = '{column}'";
            var exists = Database.SqlQueryRaw<int>(existsSql)
                .AsEnumerable().FirstOrDefault();
            if (exists == 0)
            {
                var alterSql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {decl}";
                Database.ExecuteSqlRaw(alterSql);
            }
        }

        void RemoveColumn(string table, string column)
        {
            var existsSql = $"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{table}') WHERE name = '{column}'";
            var exists = Database.SqlQueryRaw<int>(existsSql)
                .AsEnumerable().FirstOrDefault();
            if (exists != 0)
            {
                var alterSql = $"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\"";
                Database.ExecuteSqlRaw(alterSql);
            }
        }

        // EnsureCreated() eski (allaqachon yaratilgan) bazalarga yangi DbSet uchun jadval
        // ham qo'shmaydi — shu sabab FloraFaunaCodes jadvalini qo'lda, xavfsiz tarzda yaratamiz.
        void EnsureTable(string table, string createSql, string? indexSql = null)
        {
            var existsSql = $"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = '{table}'";
            var exists = Database.SqlQueryRaw<int>(existsSql)
                .AsEnumerable().FirstOrDefault();
            if (exists != 0) return;

            Database.ExecuteSqlRaw(createSql);
            if (indexSql != null) Database.ExecuteSqlRaw(indexSql);
        }

        EnsureTable("FloraFaunaCodes",
            "CREATE TABLE \"FloraFaunaCodes\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_FloraFaunaCodes\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Code\" INTEGER NOT NULL, " +
            "\"Name\" TEXT NOT NULL, " +
            "\"NameRu\" TEXT NULL, " +
            "\"PatternKey\" TEXT NULL)",
            "CREATE UNIQUE INDEX \"IX_FloraFaunaCodes_Code\" ON \"FloraFaunaCodes\" (\"Code\")");

        EnsureTable("IronHydroxideCodes",
            "CREATE TABLE \"IronHydroxideCodes\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_IronHydroxideCodes\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Code\" INTEGER NOT NULL, " +
            "\"Name\" TEXT NOT NULL, " +
            "\"NameRu\" TEXT NULL, " +
            "\"PatternKey\" TEXT NULL)",
            "CREATE UNIQUE INDEX \"IX_IronHydroxideCodes_Code\" ON \"IronHydroxideCodes\" (\"Code\")");

        EnsureTable("ClasticMaterialCodes",
            "CREATE TABLE \"ClasticMaterialCodes\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_ClasticMaterialCodes\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Code\" INTEGER NOT NULL, " +
            "\"Name\" TEXT NOT NULL, " +
            "\"NameRu\" TEXT NULL, " +
            "\"PatternKey\" TEXT NULL)",
            "CREATE UNIQUE INDEX \"IX_ClasticMaterialCodes_Code\" ON \"ClasticMaterialCodes\" (\"Code\")");

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
        RemoveColumn("JournalRows", "CarbonateCo2");
        AddColumn("DescriptionTemplates", "LithoCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "ColorCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "TextureCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "MineralCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "GrainSize", "TEXT NULL");
        AddColumn("SampleRows", "SampleTypeCode", "INTEGER NULL");
    }
}
