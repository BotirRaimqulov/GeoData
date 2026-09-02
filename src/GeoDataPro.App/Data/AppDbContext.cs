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
    public DbSet<Zone> Zones => Set<Zone>();
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
            var exists = Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{table}') WHERE name = '{column}'")
                .AsEnumerable().FirstOrDefault();
            if (exists == 0)
                Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {decl}");
        }

        AddColumn("LithoCodes", "NameRu", "TEXT NULL");
        AddColumn("ColorCodes", "NameRu", "TEXT NULL");
        AddColumn("TextureCodes", "NameRu", "TEXT NULL");
        AddColumn("MineralCodes", "NameRu", "TEXT NULL");
    }
}
