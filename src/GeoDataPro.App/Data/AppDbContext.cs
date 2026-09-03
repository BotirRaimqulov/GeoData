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

        AddColumn("LithoCodes", "NameRu", "TEXT NULL");
        AddColumn("ColorCodes", "NameRu", "TEXT NULL");
        AddColumn("TextureCodes", "NameRu", "TEXT NULL");
        AddColumn("MineralCodes", "NameRu", "TEXT NULL");

        AddColumn("JournalRows", "MineralCode", "INTEGER NULL");
        AddColumn("JournalRows", "GrainSize", "TEXT NULL");
        RemoveColumn("JournalRows", "Hardness");
        RemoveColumn("JournalRows", "CarbonateCo2");
        AddColumn("DescriptionTemplates", "LithoCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "ColorCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "TextureCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "MineralCode", "INTEGER NULL");
        AddColumn("DescriptionTemplates", "GrainSize", "TEXT NULL");
        AddColumn("SampleRows", "SampleTypeCode", "INTEGER NULL");
    }
}
