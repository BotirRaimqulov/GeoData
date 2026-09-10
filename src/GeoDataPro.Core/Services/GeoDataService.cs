using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeoDataPro.Core.Audit;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.Core.Services;

public interface IGeoDataService
{
    IReadOnlyList<Project> GetProjects();
    IReadOnlyList<Well> GetWells(int projectId);
    IReadOnlyList<JournalRow> GetJournalRows(int wellId);
    IReadOnlyList<SampleRow> GetSampleRows(int wellId);
    IReadOnlyList<SrpRow> GetSrpRows(int wellId);
    ReferenceSnapshot GetReferences();

    Project CreateProject(string name);
    Well CreateWell(int projectId, string number);
    void UpdateWell(Well edited);
    void SoftDeleteWell(int wellId);
    void PurgeWell(int wellId);
    void RestoreWell(int wellId);

    void SaveJournal(int wellId, IReadOnlyList<JournalRow> rows);
    void SaveSamples(int wellId, IReadOnlyList<SampleRow> rows);
    void SaveSrp(int wellId, IReadOnlyList<SrpRow> rows);
    void SaveReferences(ReferenceKind kind, IReadOnlyList<object> items);

    int CountJournalRows(int wellId);
    int CountSampleRows(int wellId);
}

public enum ReferenceKind
{
    Litho = 0,
    Color,
    Texture,
    Mineral,
    FloraFauna,
    IronHydroxide,
    ClasticMaterial,
    Description,
}

public sealed class ReferenceSnapshot
{
    public IReadOnlyList<LithoCode> Litho { get; init; } = Array.Empty<LithoCode>();
    public IReadOnlyList<ColorCode> Colors { get; init; } = Array.Empty<ColorCode>();
    public IReadOnlyList<TextureCode> Textures { get; init; } = Array.Empty<TextureCode>();
    public IReadOnlyList<MineralCode> Minerals { get; init; } = Array.Empty<MineralCode>();
    public IReadOnlyList<FloraFaunaCode> FloraFauna { get; init; } = Array.Empty<FloraFaunaCode>();
    public IReadOnlyList<IronHydroxideCode> IronHydroxides { get; init; } = Array.Empty<IronHydroxideCode>();
    public IReadOnlyList<ClasticMaterialCode> ClasticMaterials { get; init; } = Array.Empty<ClasticMaterialCode>();
    public IReadOnlyList<DescriptionTemplate> Descriptions { get; init; } = Array.Empty<DescriptionTemplate>();
}

public sealed class GeoDataService : IGeoDataService
{
    const int MaxRowsPerWell = 100_000;
    const int MaxTextLength = 4000;
    const double MaxDepth = 25_000d;

    readonly IDbContextFactory _factory;
    readonly IAuthorizationService _authz;
    readonly IAuditService _audit;
    readonly IIntegrityStamper _stamper;
    readonly IClock _clock;

    public GeoDataService(
        IDbContextFactory factory,
        IAuthorizationService authz,
        IAuditService audit,
        IIntegrityStamper stamper,
        IClock? clock = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _authz = authz ?? throw new ArgumentNullException(nameof(authz));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _stamper = stamper ?? throw new ArgumentNullException(nameof(stamper));
        _clock = clock ?? SystemClock.Instance;
    }

    public IReadOnlyList<Project> GetProjects()
    {
        _authz.Require(Permissions.ProjectRead);
        using var db = _factory.Create();
        return db.Projects.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToList();
    }

    public IReadOnlyList<Well> GetWells(int projectId)
    {
        _authz.Require(Permissions.ProjectRead);
        using var db = _factory.Create();
        return db.Wells.AsNoTracking()
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .OrderBy(x => x.Number)
            .ToList();
    }

    public IReadOnlyList<JournalRow> GetJournalRows(int wellId)
    {
        _authz.Require(Permissions.SampleRead);
        using var db = _factory.Create();
        return db.JournalRows.AsNoTracking()
            .Where(x => x.WellId == wellId && !x.IsDeleted)
            .OrderBy(x => x.OrderNo).ThenBy(x => x.Top)
            .ToList();
    }

    public IReadOnlyList<SampleRow> GetSampleRows(int wellId)
    {
        _authz.Require(Permissions.SampleRead);
        using var db = _factory.Create();
        return db.SampleRows.AsNoTracking()
            .Where(x => x.WellId == wellId && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Top)
            .ToList();
    }

    public IReadOnlyList<SrpRow> GetSrpRows(int wellId)
    {
        _authz.Require(Permissions.SampleRead);
        using var db = _factory.Create();
        return db.SrpRows.AsNoTracking()
            .Where(x => x.WellId == wellId && !x.IsDeleted)
            .OrderBy(x => x.Md)
            .ToList();
    }

    public ReferenceSnapshot GetReferences()
    {
        _authz.Require(Permissions.ReferenceRead);
        using var db = _factory.Create();
        return new ReferenceSnapshot
        {
            Litho = db.LithoCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            Colors = db.ColorCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            Textures = db.TextureCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            Minerals = db.MineralCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            FloraFauna = db.FloraFaunaCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            IronHydroxides = db.IronHydroxideCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            ClasticMaterials = db.ClasticMaterialCodes.AsNoTracking().OrderBy(x => x.Code).ToList(),
            Descriptions = db.DescriptionTemplates.AsNoTracking().OrderBy(x => x.Text).ToList(),
        };
    }

    public int CountJournalRows(int wellId)
    {
        _authz.Require(Permissions.SampleRead);
        using var db = _factory.Create();
        return db.JournalRows.Count(x => x.WellId == wellId && !x.IsDeleted);
    }

    public int CountSampleRows(int wellId)
    {
        _authz.Require(Permissions.SampleRead);
        using var db = _factory.Create();
        return db.SampleRows.Count(x => x.WellId == wellId && !x.IsDeleted);
    }

    public Project CreateProject(string name)
    {
        var actor = _authz.Require(Permissions.ProjectCreate);
        var clean = Text(name, 200) ?? throw new ArgumentException(null, nameof(name));

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        var project = new Project { Name = clean };
        Stamp(project);
        db.Projects.Add(project);
        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Create, AuditResults.Success, "Project",
            project.Id.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username));
        return project;
    }

    public Well CreateWell(int projectId, string number)
    {
        var actor = _authz.Require(Permissions.ProjectUpdate);
        var clean = Text(number, 100) ?? throw new ArgumentException(null, nameof(number));

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        if (!db.Projects.Any(x => x.Id == projectId && !x.IsDeleted))
            throw new InvalidOperationException("E_NOTFOUND");

        var well = new Well { ProjectId = projectId, Number = clean };
        Stamp(well);
        db.Wells.Add(well);
        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Create, AuditResults.Success, "Well",
            well.Id.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username));
        return well;
    }

    public void UpdateWell(Well edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var actor = _authz.Require(Permissions.ProjectUpdate);

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        var well = db.Wells.FirstOrDefault(x => x.Id == edited.Id && !x.IsDeleted)
            ?? throw new InvalidOperationException("E_NOTFOUND");

        VerifyStamp("Well", well, well.Stamp, WellFields(well));

        well.Number = Text(edited.Number, 100) ?? well.Number;
        well.RigNumber = Text(edited.RigNumber, 100);
        well.StartDepth = Depth(edited.StartDepth);
        well.EndDepth = Depth(edited.EndDepth);
        well.StartDate = Text(edited.StartDate, 40);
        well.EndDate = Text(edited.EndDate, 40);
        well.Geologist = Text(edited.Geologist, 200);
        well.Notes = Text(edited.Notes, MaxTextLength);

        if (well.StartDepth.HasValue && well.EndDepth.HasValue && well.EndDepth < well.StartDepth)
            throw new InvalidOperationException("E_RANGE");

        Stamp(well);
        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Update, AuditResults.Success, "Well",
            well.Id.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username));
    }

    public void SoftDeleteWell(int wellId)
    {
        var actor = _authz.Require(Permissions.ProjectDelete);
        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        var well = db.Wells.FirstOrDefault(x => x.Id == wellId && !x.IsDeleted)
            ?? throw new InvalidOperationException("E_NOTFOUND");

        MarkDeleted(well, actor.UserId, now);
        foreach (var row in db.JournalRows.Where(x => x.WellId == wellId && !x.IsDeleted)) MarkDeleted(row, actor.UserId, now);
        foreach (var row in db.SampleRows.Where(x => x.WellId == wellId && !x.IsDeleted)) MarkDeleted(row, actor.UserId, now);
        foreach (var row in db.SrpRows.Where(x => x.WellId == wellId && !x.IsDeleted)) MarkDeleted(row, actor.UserId, now);

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Delete, AuditResults.Success, "Well",
            wellId.ToString(CultureInfo.InvariantCulture), "soft", actor.UserId, actor.Username));
    }

    public void RestoreWell(int wellId)
    {
        var actor = _authz.Require(Permissions.ProjectUpdate);

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        var well = db.Wells.FirstOrDefault(x => x.Id == wellId && x.IsDeleted)
            ?? throw new InvalidOperationException("E_NOTFOUND");

        Unmark(well);
        foreach (var row in db.JournalRows.Where(x => x.WellId == wellId && x.IsDeleted)) Unmark(row);
        foreach (var row in db.SampleRows.Where(x => x.WellId == wellId && x.IsDeleted)) Unmark(row);
        foreach (var row in db.SrpRows.Where(x => x.WellId == wellId && x.IsDeleted)) Unmark(row);

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Restore, AuditResults.Success, "Well",
            wellId.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username));
    }

    public void PurgeWell(int wellId)
    {
        var actor = _authz.Require(Permissions.DataDeletePermanent);

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        var well = db.Wells.FirstOrDefault(x => x.Id == wellId)
            ?? throw new InvalidOperationException("E_NOTFOUND");

        var journal = db.JournalRows.Where(x => x.WellId == wellId).ToList();
        var samples = db.SampleRows.Where(x => x.WellId == wellId).ToList();
        var srp = db.SrpRows.Where(x => x.WellId == wellId).ToList();

        Archive(db, "Well", well.Id, new { well, journal, samples, srp }, actor.UserId);

        db.JournalRows.RemoveRange(journal);
        db.SampleRows.RemoveRange(samples);
        db.SrpRows.RemoveRange(srp);
        db.Wells.Remove(well);

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.DeletePermanent, AuditResults.Success, "Well",
            wellId.ToString(CultureInfo.InvariantCulture), null, actor.UserId, actor.Username));
    }

    public void SaveJournal(int wellId, IReadOnlyList<JournalRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var actor = _authz.Require(Permissions.SampleWrite);
        if (rows.Count > MaxRowsPerWell) throw new InvalidOperationException("E_TOOMANY");

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        RequireWell(db, wellId);

        var existing = db.JournalRows.Where(x => x.WellId == wellId).ToDictionary(x => x.Id);
        var keep = rows.Where(x => x.Id != 0).Select(x => x.Id).ToHashSet();
        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var gone in existing.Values.Where(x => !keep.Contains(x.Id) && !x.IsDeleted))
            MarkDeleted(gone, actor.UserId, now);

        int order = 1;
        foreach (var candidate in rows)
        {
            Normalize(candidate);
            if (candidate.Id == 0)
            {
                candidate.WellId = wellId;
                candidate.OrderNo = order++;
                candidate.IsDeleted = false;
                Stamp(candidate);
                db.JournalRows.Add(candidate);
                continue;
            }

            if (!existing.TryGetValue(candidate.Id, out var tracked)) throw new InvalidOperationException("E_NOTFOUND");
            if (tracked.WellId != wellId) throw new SecurityDeniedException(Permissions.SampleWrite);

            VerifyStamp("JournalRow", tracked, tracked.Stamp, JournalFields(tracked));

            tracked.OrderNo = order++;
            tracked.Top = candidate.Top;
            tracked.Bottom = candidate.Bottom;
            tracked.CoreRecoveryM = candidate.CoreRecoveryM;
            tracked.ZoneName = candidate.ZoneName;
            tracked.LithoCode = candidate.LithoCode;
            tracked.ColorCode = candidate.ColorCode;
            tracked.IronHydroxideCode = candidate.IronHydroxideCode;
            tracked.Composition = candidate.Composition;
            tracked.ClasticMaterialCodes = candidate.ClasticMaterialCodes;
            tracked.TextureCode = candidate.TextureCode;
            tracked.GrainSize = candidate.GrainSize;
            tracked.Hardness = candidate.Hardness;
            tracked.Cementation = candidate.Cementation;
            tracked.MineralCode = candidate.MineralCode;
            tracked.FloraFaunaCode = candidate.FloraFaunaCode;
            tracked.Description = candidate.Description;
            tracked.IsDeleted = false;
            tracked.DeletedUtc = null;
            tracked.DeletedByUserId = null;
            Stamp(tracked);
        }

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Update, AuditResults.Success, "JournalRow",
            wellId.ToString(CultureInfo.InvariantCulture),
            rows.Count.ToString(CultureInfo.InvariantCulture), actor.UserId, actor.Username));
    }

    public void SaveSamples(int wellId, IReadOnlyList<SampleRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var actor = _authz.Require(Permissions.SampleWrite);
        if (rows.Count > MaxRowsPerWell) throw new InvalidOperationException("E_TOOMANY");

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        RequireWell(db, wellId);

        var existing = db.SampleRows.Where(x => x.WellId == wellId).ToDictionary(x => x.Id);
        var keep = rows.Where(x => x.Id != 0).Select(x => x.Id).ToHashSet();
        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var gone in existing.Values.Where(x => !keep.Contains(x.Id) && !x.IsDeleted))
            MarkDeleted(gone, actor.UserId, now);

        int order = 1;
        foreach (var candidate in rows)
        {
            Normalize(candidate);
            if (candidate.Id == 0)
            {
                candidate.WellId = wellId;
                candidate.DisplayOrder = order++;
                candidate.IsDeleted = false;
                Stamp(candidate);
                db.SampleRows.Add(candidate);
                continue;
            }

            if (!existing.TryGetValue(candidate.Id, out var tracked)) throw new InvalidOperationException("E_NOTFOUND");
            if (tracked.WellId != wellId) throw new SecurityDeniedException(Permissions.SampleWrite);

            VerifyStamp("SampleRow", tracked, tracked.Stamp, SampleFields(tracked));

            tracked.SampleNumber = candidate.SampleNumber;
            tracked.SampleTypeCode = candidate.SampleTypeCode;
            tracked.Top = candidate.Top;
            tracked.Bottom = candidate.Bottom;
            tracked.ZoneName = candidate.ZoneName;
            tracked.Notes = candidate.Notes;
            tracked.DisplayOrder = order++;
            tracked.IsDeleted = false;
            tracked.DeletedUtc = null;
            tracked.DeletedByUserId = null;
            Stamp(tracked);
        }

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Update, AuditResults.Success, "SampleRow",
            wellId.ToString(CultureInfo.InvariantCulture),
            rows.Count.ToString(CultureInfo.InvariantCulture), actor.UserId, actor.Username));
    }

    public void SaveSrp(int wellId, IReadOnlyList<SrpRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var actor = _authz.Require(Permissions.SampleWrite);
        if (rows.Count > MaxRowsPerWell) throw new InvalidOperationException("E_TOOMANY");

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        RequireWell(db, wellId);

        var existing = db.SrpRows.Where(x => x.WellId == wellId).ToDictionary(x => x.Id);
        var keep = rows.Where(x => x.Id != 0).Select(x => x.Id).ToHashSet();
        var now = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var gone in existing.Values.Where(x => !keep.Contains(x.Id) && !x.IsDeleted))
            MarkDeleted(gone, actor.UserId, now);

        foreach (var candidate in rows)
        {
            if (!double.IsFinite(candidate.Md) || !double.IsFinite(candidate.CoreGk))
                throw new InvalidOperationException("E_RANGE");

            if (candidate.Id == 0)
            {
                candidate.WellId = wellId;
                candidate.IsDeleted = false;
                Stamp(candidate);
                db.SrpRows.Add(candidate);
                continue;
            }

            if (!existing.TryGetValue(candidate.Id, out var tracked)) throw new InvalidOperationException("E_NOTFOUND");
            if (tracked.WellId != wellId) throw new SecurityDeniedException(Permissions.SampleWrite);

            tracked.Md = candidate.Md;
            tracked.CoreGk = candidate.CoreGk;
            tracked.IsDeleted = false;
            tracked.DeletedUtc = null;
            tracked.DeletedByUserId = null;
            Stamp(tracked);
        }

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Update, AuditResults.Success, "SrpRow",
            wellId.ToString(CultureInfo.InvariantCulture),
            rows.Count.ToString(CultureInfo.InvariantCulture), actor.UserId, actor.Username));
    }

    public void SaveReferences(ReferenceKind kind, IReadOnlyList<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var actor = _authz.Require(Permissions.ReferenceWrite);
        if (items.Count > 10_000) throw new InvalidOperationException("E_TOOMANY");

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();

        switch (kind)
        {
            case ReferenceKind.Litho: Sync(db, db.LithoCodes, items.OfType<LithoCode>().ToList(), x => x.Id); break;
            case ReferenceKind.Color: Sync(db, db.ColorCodes, items.OfType<ColorCode>().ToList(), x => x.Id); break;
            case ReferenceKind.Texture: Sync(db, db.TextureCodes, items.OfType<TextureCode>().ToList(), x => x.Id); break;
            case ReferenceKind.Mineral: Sync(db, db.MineralCodes, items.OfType<MineralCode>().ToList(), x => x.Id); break;
            case ReferenceKind.FloraFauna: Sync(db, db.FloraFaunaCodes, items.OfType<FloraFaunaCode>().ToList(), x => x.Id); break;
            case ReferenceKind.IronHydroxide: Sync(db, db.IronHydroxideCodes, items.OfType<IronHydroxideCode>().ToList(), x => x.Id); break;
            case ReferenceKind.ClasticMaterial: Sync(db, db.ClasticMaterialCodes, items.OfType<ClasticMaterialCode>().ToList(), x => x.Id); break;
            case ReferenceKind.Description: Sync(db, db.DescriptionTemplates, items.OfType<DescriptionTemplate>().ToList(), x => x.Id); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }

        db.SaveChanges();
        tx.Commit();

        _audit.Write(new AuditRecord(AuditActions.Update, AuditResults.Success, "Reference",
            kind.ToString(), items.Count.ToString(CultureInfo.InvariantCulture), actor.UserId, actor.Username));
    }

    static void Sync<T>(AppDbContext db, DbSet<T> set, IReadOnlyList<T> items, Func<T, int> id) where T : class
    {
        var existing = set.ToList();
        var existingById = existing.Where(x => id(x) != 0).ToDictionary(id);
        var keep = items.Where(x => id(x) != 0).Select(id).ToHashSet();

        foreach (var gone in existing.Where(x => !keep.Contains(id(x)))) set.Remove(gone);

        foreach (var item in items)
        {
            var entityId = id(item);
            if (entityId == 0) set.Add(item);
            else if (existingById.TryGetValue(entityId, out var tracked)) db.Entry(tracked).CurrentValues.SetValues(item);
            else throw new InvalidOperationException("E_NOTFOUND");
        }
    }

    void RequireWell(AppDbContext db, int wellId)
    {
        if (!db.Wells.Any(x => x.Id == wellId && !x.IsDeleted))
            throw new InvalidOperationException("E_NOTFOUND");
    }

    void Archive(AppDbContext db, string entity, int id, object payload, int? userId)
    {
        var json = JsonSerializer.Serialize(payload, ArchiveOptions);
        var record = new DeletedRecord
        {
            Entity = entity,
            EntityId = id.ToString(CultureInfo.InvariantCulture),
            Payload = json,
            DeletedUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            DeletedByUserId = userId,
        };
        record.Stamp = _stamper.Compute("DeletedRecord", record.EntityId, record.Entity, record.Payload, record.DeletedUtc);
        db.DeletedRecords.Add(record);
    }

    static readonly JsonSerializerOptions ArchiveOptions = new()
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
    };

    void MarkDeleted(ITrackedEntity entity, int? userId, string now)
    {
        entity.IsDeleted = true;
        entity.DeletedUtc = now;
        entity.DeletedByUserId = userId;
        Stamp(entity);
    }

    static void Unmark(ITrackedEntity entity)
    {
        entity.IsDeleted = false;
        entity.DeletedUtc = null;
        entity.DeletedByUserId = null;
    }

    void Stamp(ITrackedEntity entity)
    {
        entity.Stamp = entity switch
        {
            Well w => _stamper.Compute("Well", w.Id, WellFields(w)),
            JournalRow j => _stamper.Compute("JournalRow", j.Id, JournalFields(j)),
            SampleRow s => _stamper.Compute("SampleRow", s.Id, SampleFields(s)),
            SrpRow p => _stamper.Compute("SrpRow", p.Id, p.WellId, p.Md, p.CoreGk, p.IsDeleted),
            Project pr => _stamper.Compute("Project", pr.Id, pr.Name, pr.IsDeleted),
            _ => entity.Stamp,
        };
    }

    void VerifyStamp(string entity, ITrackedEntity target, string? stamp, object?[] fields)
    {
        if (string.IsNullOrEmpty(stamp)) return;

        var id = target switch
        {
            Well w => w.Id,
            JournalRow j => j.Id,
            SampleRow s => s.Id,
            SrpRow p => p.Id,
            Project pr => pr.Id,
            _ => 0,
        };

        if (_stamper.Verify(stamp, entity, id, fields)) return;

        _audit.Write(new AuditRecord(AuditActions.IntegrityFailure, AuditResults.Failure, entity,
            id.ToString(CultureInfo.InvariantCulture), null));
        throw new IntegrityViolationException(entity, id);
    }

    static object?[] WellFields(Well w) =>
        new object?[] { w.ProjectId, w.Number, w.RigNumber, w.StartDepth, w.EndDepth, w.StartDate, w.EndDate, w.Geologist, w.IsDeleted };

    static object?[] JournalFields(JournalRow j) =>
        new object?[]
        {
            j.WellId, j.OrderNo, j.Top, j.Bottom, j.CoreRecoveryM, j.ZoneName, j.LithoCode, j.ColorCode,
            j.IronHydroxideCode, j.Composition, j.ClasticMaterialCodes, j.TextureCode, j.GrainSize,
            j.Hardness, j.Cementation, j.MineralCode, j.FloraFaunaCode, j.Description, j.IsDeleted,
        };

    static object?[] SampleFields(SampleRow s) =>
        new object?[] { s.WellId, s.SampleNumber, s.SampleTypeCode, s.Top, s.Bottom, s.ZoneName, s.Notes, s.IsDeleted };

    void Normalize(JournalRow row)
    {
        row.Top = Require(row.Top);
        row.Bottom = Require(row.Bottom);
        row.CoreRecoveryM = Require(row.CoreRecoveryM);
        if (row.Bottom < row.Top || row.CoreRecoveryM < 0) throw new InvalidOperationException("E_RANGE");
        row.ZoneName = Text(row.ZoneName, 200);
        row.Composition = Text(row.Composition, MaxTextLength);
        row.ClasticMaterialCodes = CodeList(row.ClasticMaterialCodes);
        row.GrainSize = Text(row.GrainSize, 100);
        row.Hardness = Text(row.Hardness, 100);
        row.Cementation = Text(row.Cementation, 100);
        row.Description = Text(row.Description, MaxTextLength);
    }

    void Normalize(SampleRow row)
    {
        row.Top = Require(row.Top);
        row.Bottom = Require(row.Bottom);
        if (row.Bottom < row.Top) throw new InvalidOperationException("E_RANGE");
        row.SampleNumber = Text(row.SampleNumber, 100) ?? throw new InvalidOperationException("E_RANGE");
        row.ZoneName = Text(row.ZoneName, 200);
        row.Notes = Text(row.Notes, MaxTextLength);
    }

    static double Require(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > MaxDepth) throw new InvalidOperationException("E_RANGE");
        return value;
    }

    static double? Depth(double? value)
    {
        if (value is null) return null;
        return Require(value.Value);
    }

    static string? Text(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) trimmed = trimmed[..max];
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            buffer.Append(char.IsControl(c) && c != '\n' && c != '\t' ? ' ' : c);
        return buffer.ToString();
    }

    static string? CodeList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var codes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(64)
            .Where(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToArray();
        return codes.Length == 0 ? null : string.Join(',', codes);
    }
}
