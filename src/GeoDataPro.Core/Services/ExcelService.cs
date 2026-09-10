using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Files;

namespace GeoDataPro.Core.Services;

/// <summary>
/// Excel import/eksport — "Шаблон.xlsx" formatiga mos.
/// Multi-well: Excel dagi "Well Name" ustuniga qarab barcha quduqlarni birga import/eksport qiladi.
/// Mavjud bo'lmagan quduqlar avtomatik yaratiladi (berilgan ProjectId ostida).
/// </summary>
public static class ExcelService
{
    public const int MaxRowsPerSheet = 200_000;
    public const int MaxWellsPerWorkbook = 5_000;
    const int MaxTextCell = 4000;
    const double DepthLimit = 25_000d;

    public sealed class ImportResult
    {
        public int JournalRows { get; set; }
        public int SampleRows { get; set; }
        public int SrpRows { get; set; }
        public int WellsCreated { get; set; }
        public int WellsUpdated { get; set; }
        public List<string> WellNumbers { get; } = new();
        public string? ProjectName { get; set; }
    }

    // =====================================================================
    //  MULTI-WELL IMPORT (asosiy API)
    // =====================================================================

    /// <summary>
    /// Excel fayldan barcha quduqlarni import qiladi.
    /// <paramref name="projectId"/> — ma'lumotlar shu loyihaga yoziladi.
    /// Excel dagi Well Name bo'yicha quduq topilmasa — yangi quduq yaratiladi.
    /// Mavjud quduq uchun eski Journal/Sample/SRP qatorlari o'chiriladi va yangilari yoziladi.
    /// </summary>
    public static ImportResult ImportWorkbook(string path, int projectId, CancellationToken ct = default)
    {
        SafeFile.ValidateWorkbook(path);

        var result = new ImportResult();

        using var wb = new XLWorkbook(path);
        using var db = new AppDbContext();

        var project = db.Projects.Find(projectId)
            ?? throw new InvalidOperationException($"Loyiha topilmadi (Id={projectId}).");
        result.ProjectName = project.Name;

        // Quduq nomlarini to'plash (barcha sheetlardan)
        var wellNames = CollectWellNames(wb, ct);
        if (wellNames.Count == 0)
            throw new InvalidOperationException("E_IMPORT_NOWELLS");
        if (wellNames.Count > MaxWellsPerWorkbook)
            throw new InvalidOperationException("E_IMPORT_TOOMANY");

        using var tx = db.Database.BeginTransaction();

        // Quduqlar: mavjud bo'lsa ishlatamiz, yo'q bo'lsa yaratamiz
        var wellMap = new Dictionary<string, Well>(StringComparer.OrdinalIgnoreCase);
        var existingWells = db.Wells.Where(w => w.ProjectId == projectId && !w.IsDeleted).ToList();

        foreach (var name in wellNames)
        {
            var well = existingWells.FirstOrDefault(w =>
                string.Equals(w.Number, name, StringComparison.OrdinalIgnoreCase));

            if (well == null)
            {
                well = new Well { ProjectId = projectId, Number = name };
                db.Wells.Add(well);
                db.SaveChanges(); // Id olish uchun
                result.WellsCreated++;
            }
            else
            {
                result.WellsUpdated++;
            }

            wellMap[name] = well;
            result.WellNumbers.Add(well.Number);
        }

        result.JournalRows = ImportJournalMulti(wb, db, wellMap, ct);
        result.SampleRows = ImportSamplesMulti(wb, db, wellMap, ct);
        result.SrpRows = ImportSrpMulti(wb, db, wellMap, ct);

        db.SaveChanges();
        tx.Commit();
        return result;
    }

    /// <summary>
    /// Mavjud loyihaga yoki yangi yaratilgan loyihaga import.
    /// <paramref name="projectId"/> null bo'lsa va <paramref name="newProjectName"/> berilsa — yangi loyiha yaratiladi.
    /// </summary>
    public static ImportResult ImportWorkbookToProject(
        string path, int? projectId, string? newProjectName, CancellationToken ct = default)
    {
        SafeFile.ValidateWorkbook(path);
        using var db = new AppDbContext();

        int targetProjectId;
        if (projectId.HasValue && projectId.Value > 0)
        {
            if (!db.Projects.Any(p => p.Id == projectId.Value && !p.IsDeleted))
                throw new InvalidOperationException("E_NOTFOUND");
            targetProjectId = projectId.Value;
        }
        else if (!string.IsNullOrWhiteSpace(newProjectName))
        {
            var name = newProjectName.Trim();
            if (name.Length > 200) name = name[..200];
            var existing = db.Projects.FirstOrDefault(p =>
                !p.IsDeleted && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                targetProjectId = existing.Id;
            }
            else
            {
                var p = new Project { Name = name };
                db.Projects.Add(p);
                db.SaveChanges();
                targetProjectId = p.Id;
            }
        }
        else
        {
            throw new InvalidOperationException("E_IMPORT_NOPROJECT");
        }

        return ImportWorkbook(path, targetProjectId, ct);
    }

    // =====================================================================
    //  MULTI-WELL EXPORT
    // =====================================================================

    /// <summary>
    /// Bitta loyihadagi barcha (yoki berilgan) quduqlarni bitta Excel faylga eksport qiladi.
    /// </summary>
    public static void ExportWorkbookMulti(
        string path, int projectId, IEnumerable<int>? wellIds = null, CancellationToken ct = default)
    {
        using var db = new AppDbContext();
        var project = db.Projects.FirstOrDefault(p => p.Id == projectId && !p.IsDeleted)
            ?? throw new InvalidOperationException("E_NOTFOUND");

        List<Well> wells;
        if (wellIds != null)
        {
            var idSet = wellIds.ToHashSet();
            wells = db.Wells
                .Where(w => w.ProjectId == projectId && !w.IsDeleted && idSet.Contains(w.Id))
                .OrderBy(w => w.Number)
                .ToList();
        }
        else
        {
            wells = db.Wells
                .Where(w => w.ProjectId == projectId && !w.IsDeleted)
                .OrderBy(w => w.Number)
                .ToList();
        }

        if (wells.Count == 0)
            throw new InvalidOperationException("E_NOTFOUND");

        using var wb = new XLWorkbook();

        // --- Dala jurnali (barcha quduqlar bir sheetda) ---
        {
            var ws = wb.AddWorksheet("Dala jurnali");
            string[] head = { "Well Name", "TOP", "BOTTOM", "CoreRecoveryM", "Zone name", "Litho_Codes", "Core color", "Core Description" };
            for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            ws.Cell(2, 2).Value = "m";
            ws.Cell(2, 3).Value = "m";
            ws.Cell(2, 4).Value = "m";
            ws.Row(1).Style.Font.Bold = true;

            int r = 3;
            foreach (var well in wells)
            {
                ct.ThrowIfCancellationRequested();
                var journal = db.JournalRows
                    .Where(j => j.WellId == well.Id && !j.IsDeleted)
                    .OrderBy(j => j.OrderNo)
                    .ToList();
                foreach (var j in journal)
                {
                    ws.Cell(r, 1).Value = well.Number;
                    ws.Cell(r, 2).Value = j.Top;
                    ws.Cell(r, 3).Value = j.Bottom;
                    ws.Cell(r, 4).Value = j.CoreRecoveryM;
                    ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(j.ZoneName) ? j.OrderNo.ToString() : j.ZoneName;
                    ws.Cell(r, 6).Value = j.LithoCode;
                    ws.Cell(r, 7).Value = j.ColorCode;
                    ws.Cell(r, 8).Value = j.Description;
                    r++;
                }
            }
            ws.Columns().AdjustToContents();
        }

        // --- SRP ---
        {
            var ws = wb.AddWorksheet("SRP");
            ws.Cell(1, 1).Value = "Well Name";
            ws.Cell(1, 2).Value = "MD";
            ws.Cell(1, 3).Value = "Core_GK";
            ws.Cell(1, 4).Value = "Zone name";
            ws.Cell(2, 2).Value = "m";
            ws.Row(1).Style.Font.Bold = true;

            int r = 3;
            foreach (var well in wells)
            {
                ct.ThrowIfCancellationRequested();
                var srp = db.SrpRows
                    .Where(p => p.WellId == well.Id && !p.IsDeleted)
                    .OrderBy(p => p.Md)
                    .ToList();
                int orderNo = 1;
                foreach (var p in srp)
                {
                    ws.Cell(r, 1).Value = well.Number;
                    ws.Cell(r, 2).Value = p.Md;
                    ws.Cell(r, 3).Value = p.CoreGk;
                    ws.Cell(r, 4).Value = orderNo.ToString();
                    r++;
                    orderNo++;
                }
            }
            ws.Columns().AdjustToContents();
        }

        // --- Namuna (umumiy + turlarga bo'lingan) ---
        {
            var allSamples = new List<(Well well, SampleRow sample)>();
            foreach (var well in wells)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var s in db.SampleRows.Where(x => x.WellId == well.Id && !x.IsDeleted).OrderBy(x => x.Top))
                    allSamples.Add((well, s));
            }

            WriteSamplesWorksheetMulti(wb, "Namuna", allSamples);

            var typeGroups = new (int Code, string SheetName)[]
            {
                (11, "Namuna_11"),
                (12, "Namuna_12"),
                (0,  "Namuna_0"),
                (4,  "Namuna_4"),
            };
            foreach (var (code, sheetName) in typeGroups)
            {
                var group = allSamples
                    .Where(x => x.sample.SampleTypeCode.HasValue && x.sample.SampleTypeCode.Value == code)
                    .OrderBy(x => x.well.Number)
                    .ThenBy(x => x.sample.Top)
                    .ToList();
                WriteSamplesWorksheetMulti(wb, sheetName, group);
            }
        }

        wb.SaveAs(path);
    }

    // =====================================================================
    //  LEGACY single-well API (orqaga moslik uchun saqlanadi)
    // =====================================================================

    public static int ImportJournal(string path, int wellId)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(s =>
                s.Name.Contains("Dala", StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("jurnal", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);

            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();
            Retire(db.JournalRows.Where(r => r.WellId == wellId && !r.IsDeleted));

            int order = 1, added = 0;
            foreach (var row in ws.RowsUsed().Skip(2))
            {
                var top = row.Cell(2).GetValue<double?>();
                var bot = row.Cell(3).GetValue<double?>();
                if (top is null || bot is null) continue;
                db.JournalRows.Add(new JournalRow
                {
                    WellId = wellId,
                    OrderNo = order++,
                    Top = top.Value,
                    Bottom = bot.Value,
                    CoreRecoveryM = row.Cell(4).GetValue<double?>() ?? 0,
                    ZoneName = row.Cell(5).GetString().Trim().NullIfEmpty(),
                    LithoCode = row.Cell(6).GetValue<int?>(),
                    ColorCode = row.Cell(7).GetValue<int?>(),
                    Description = row.Cell(8).GetString().Trim().NullIfEmpty(),
                });
                added++;
            }

            db.SaveChanges();
            tx.Commit();
            return added;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dala jurnali importida xato yuz berdi.", ex);
        }
    }

    public static void ExportJournal(string path, Well well, IEnumerable<JournalRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Dala jurnali");
        string[] head = { "Well Name", "TOP", "BOTTOM", "CoreRecoveryM", "Zone name", "Litho_Codes", "Core color", "Core Description" };
        for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        ws.Cell(2, 2).Value = "m"; ws.Cell(2, 3).Value = "m"; ws.Cell(2, 4).Value = "m";
        ws.Row(1).Style.Font.Bold = true;

        int r = 3;
        foreach (var j in rows.Where(x => !x.IsDeleted).OrderBy(x => x.OrderNo))
        {
            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = j.Top;
            ws.Cell(r, 3).Value = j.Bottom;
            ws.Cell(r, 4).Value = j.CoreRecoveryM;
            ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(j.ZoneName) ? j.OrderNo.ToString() : j.ZoneName;
            ws.Cell(r, 6).Value = j.LithoCode;
            ws.Cell(r, 7).Value = j.ColorCode;
            ws.Cell(r, 8).Value = j.Description;
            r++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    public static int ImportSrp(string path, int wellId, string wellNumber)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name.Contains("SRP", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);
            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();
            Retire(db.SrpRows.Where(r => r.WellId == wellId && !r.IsDeleted));
            int added = 0;
            foreach (var row in ws.RowsUsed().Skip(2))
            {
                var md = row.Cell(2).GetValue<double?>();
                var gk = row.Cell(3).GetValue<double?>();
                if (md is null || gk is null) continue;
                db.SrpRows.Add(new SrpRow { WellId = wellId, Md = md.Value, CoreGk = gk.Value });
                added++;
            }
            db.SaveChanges();
            tx.Commit();
            return added;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SRP importida xato yuz berdi.", ex);
        }
    }

    public static void ExportSrp(string path, Well well, IEnumerable<SrpRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("SRP");
        ws.Cell(1, 1).Value = "Well Name";
        ws.Cell(1, 2).Value = "MD";
        ws.Cell(1, 3).Value = "Core_GK";
        ws.Cell(1, 4).Value = "Zone name";
        ws.Cell(2, 2).Value = "m";
        ws.Row(1).Style.Font.Bold = true;
        int r = 3;
        int orderNo = 1;
        foreach (var p in rows.Where(x => !x.IsDeleted).OrderBy(x => x.Md))
        {
            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = p.Md;
            ws.Cell(r, 3).Value = p.CoreGk;
            ws.Cell(r, 4).Value = orderNo.ToString();
            r++;
            orderNo++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    public static int ImportSamples(string path, int wellId)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();
            Retire(db.SampleRows.Where(r => r.WellId == wellId && !r.IsDeleted));

            var sheetNames = new[] { "Namuna", "Namuna_11", "Namuna_12", "Namuna_0", "Namuna_4" };
            var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;

            foreach (var sheetName in sheetNames)
            {
                var ws = wb.Worksheets.FirstOrDefault(s =>
                    s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) continue;

                int? typeCode = sheetName switch
                {
                    "Namuna_11" => 11,
                    "Namuna_12" => 12,
                    "Namuna_0" => 0,
                    "Namuna_4" => 4,
                    _ => null
                };

                foreach (var row in ws.RowsUsed().Skip(2))
                {
                    var num = row.Cell(2).GetString().Trim();
                    var top = row.Cell(3).GetValue<double?>();
                    var bot = row.Cell(4).GetValue<double?>();
                    if (string.IsNullOrWhiteSpace(num) || top is null || bot is null) continue;
                    if (!seenNumbers.Add(num)) continue;

                    db.SampleRows.Add(new SampleRow
                    {
                        WellId = wellId,
                        SampleNumber = num,
                        Top = top.Value,
                        Bottom = bot.Value,
                        SampleTypeCode = typeCode,
                        ZoneName = row.Cell(6).GetString().Trim().NullIfEmpty(),
                    });
                    added++;
                }
            }

            db.SaveChanges();
            tx.Commit();
            return added;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Namuna importida xato yuz berdi.", ex);
        }
    }

    public static void ExportSamples(string path, Well well, IEnumerable<SampleRow> rows)
    {
        using var wb = new XLWorkbook();
        var allRows = rows.ToList();

        var typeGroups = new (int Code, string SheetName)[]
        {
            (11, "Namuna_11"),
            (12, "Namuna_12"),
            (0,  "Namuna_0"),
            (4,  "Namuna_4"),
        };

        WriteSamplesWorksheet(wb, "Namuna", well, allRows);

        foreach (var (code, sheetName) in typeGroups)
        {
            var groupRows = allRows
                .Where(r => r.SampleTypeCode.HasValue && r.SampleTypeCode.Value == code)
                .OrderBy(r => r.Top)
                .ToList();
            WriteSamplesWorksheet(wb, sheetName, well, groupRows);
        }

        wb.SaveAs(path);
    }

    public static void ExportWorkbook(string path, Well well, CancellationToken ct = default)
    {
        using var db = new AppDbContext();
        ct.ThrowIfCancellationRequested();
        var journal = db.JournalRows.Where(r => r.WellId == well.Id && !r.IsDeleted).OrderBy(r => r.OrderNo).ToList();
        var samples = db.SampleRows.Where(r => r.WellId == well.Id && !r.IsDeleted).OrderBy(r => r.Top).ToList();
        var srp = db.SrpRows.Where(r => r.WellId == well.Id && !r.IsDeleted).OrderBy(r => r.Md).ToList();

        using var wb = new XLWorkbook();
        {
            var ws = wb.AddWorksheet("Dala jurnali");
            string[] head = { "Well Name", "TOP", "BOTTOM", "CoreRecoveryM", "Zone name", "Litho_Codes", "Core color", "Core Description" };
            for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            ws.Row(1).Style.Font.Bold = true;
            int r = 3;
            foreach (var j in journal)
            {
                ws.Cell(r, 1).Value = well.Number; ws.Cell(r, 2).Value = j.Top; ws.Cell(r, 3).Value = j.Bottom;
                ws.Cell(r, 4).Value = j.CoreRecoveryM;
                ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(j.ZoneName) ? j.OrderNo.ToString() : j.ZoneName;
                ws.Cell(r, 6).Value = j.LithoCode; ws.Cell(r, 7).Value = j.ColorCode; ws.Cell(r, 8).Value = j.Description;
                r++;
            }
            ws.Columns().AdjustToContents();
        }
        {
            var ws = wb.AddWorksheet("SRP");
            ws.Cell(1, 1).Value = "Well Name";
            ws.Cell(1, 2).Value = "MD";
            ws.Cell(1, 3).Value = "Core_GK";
            ws.Cell(1, 4).Value = "Zone name";
            ws.Row(1).Style.Font.Bold = true;
            int r = 3;
            int orderNo = 1;
            foreach (var p in srp)
            {
                ws.Cell(r, 1).Value = well.Number;
                ws.Cell(r, 2).Value = p.Md;
                ws.Cell(r, 3).Value = p.CoreGk;
                ws.Cell(r, 4).Value = orderNo.ToString();
                r++;
                orderNo++;
            }
            ws.Columns().AdjustToContents();
        }
        {
            WriteSamplesWorksheet(wb, "Namuna", well, samples);

            var typeGroups = new (int Code, string SheetName)[]
            {
                (11, "Namuna_11"),
                (12, "Namuna_12"),
                (0,  "Namuna_0"),
                (4,  "Namuna_4"),
            };
            foreach (var (code, sheetName) in typeGroups)
            {
                var groupRows = samples
                    .Where(r => r.SampleTypeCode.HasValue && r.SampleTypeCode.Value == code)
                    .OrderBy(r => r.Top)
                    .ToList();
                WriteSamplesWorksheet(wb, sheetName, well, groupRows);
            }
        }
        wb.SaveAs(path);
    }

    // =====================================================================
    //  Internal helpers
    // =====================================================================

    static HashSet<string> CollectWellNames(XLWorkbook wb, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void FromSheet(IXLWorksheet? ws, int wellCol = 1)
        {
            if (ws == null) return;
            int seen = 0;
            foreach (var row in ws.RowsUsed().Skip(2))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > MaxRowsPerSheet) throw new InvalidOperationException("E_IMPORT_TOOMANY");
                var name = WellName(row.Cell(wellCol).GetString());
                if (name != null) names.Add(name);
            }
        }

        var journal = wb.Worksheets.FirstOrDefault(s =>
            s.Name.Contains("Dala", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("jurnal", StringComparison.OrdinalIgnoreCase));
        FromSheet(journal ?? wb.Worksheets.FirstOrDefault());

        FromSheet(wb.Worksheets.FirstOrDefault(s => s.Name.Contains("SRP", StringComparison.OrdinalIgnoreCase)));

        foreach (var sheetName in new[] { "Namuna", "Namuna_11", "Namuna_12", "Namuna_0", "Namuna_4" })
        {
            FromSheet(wb.Worksheets.FirstOrDefault(s =>
                s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)));
        }

        return names;
    }

    static int ImportJournalMulti(XLWorkbook wb, AppDbContext db, Dictionary<string, Well> wellMap, CancellationToken ct)
    {
        var ws = wb.Worksheets.FirstOrDefault(s =>
            s.Name.Contains("Dala", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("jurnal", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);

        foreach (var well in wellMap.Values)
            Retire(db.JournalRows.Where(r => r.WellId == well.Id && !r.IsDeleted));

        var orderByWell = wellMap.Values.ToDictionary(w => w.Id, _ => 1);
        int added = 0;
        int seen = 0;

        foreach (var row in ws.RowsUsed().Skip(2))
        {
            ct.ThrowIfCancellationRequested();
            if (++seen > MaxRowsPerSheet) throw new InvalidOperationException("E_IMPORT_TOOMANY");

            var wellName = WellName(row.Cell(1).GetString());
            if (wellName is null) continue;
            if (!wellMap.TryGetValue(wellName, out var well)) continue;

            var top = Depth(row.Cell(2).GetValue<double?>());
            var bot = Depth(row.Cell(3).GetValue<double?>());
            if (top is null || bot is null) continue;
            if (bot < top) throw new InvalidOperationException("E_IMPORT_RANGE");

            var recovery = Depth(row.Cell(4).GetValue<double?>()) ?? 0;
            if (recovery < 0) throw new InvalidOperationException("E_IMPORT_RANGE");

            int order = orderByWell[well.Id];
            db.JournalRows.Add(new JournalRow
            {
                WellId = well.Id,
                OrderNo = order,
                Top = top.Value,
                Bottom = bot.Value,
                CoreRecoveryM = recovery,
                ZoneName = Cell(row.Cell(5).GetString(), 200),
                LithoCode = Code(row.Cell(6).GetValue<int?>()),
                ColorCode = Code(row.Cell(7).GetValue<int?>()),
                Description = Cell(row.Cell(8).GetString()),
            });
            orderByWell[well.Id] = order + 1;
            added++;
        }

        return added;
    }

    static int ImportSamplesMulti(XLWorkbook wb, AppDbContext db, Dictionary<string, Well> wellMap, CancellationToken ct)
    {
        foreach (var well in wellMap.Values)
            Retire(db.SampleRows.Where(r => r.WellId == well.Id && !r.IsDeleted));

        int seen = 0;

        var sheetNames = new[] { "Namuna", "Namuna_11", "Namuna_12", "Namuna_0", "Namuna_4" };
        var unique = new HashSet<(int, string)>();
        int added = 0;

        foreach (var sheetName in sheetNames)
        {
            var ws = wb.Worksheets.FirstOrDefault(s =>
                s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            if (ws == null) continue;

            int? typeCode = sheetName switch
            {
                "Namuna_11" => 11,
                "Namuna_12" => 12,
                "Namuna_0" => 0,
                "Namuna_4" => 4,
                _ => null
            };

            foreach (var row in ws.RowsUsed().Skip(2))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > MaxRowsPerSheet) throw new InvalidOperationException("E_IMPORT_TOOMANY");

                var wellName = WellName(row.Cell(1).GetString());
                if (wellName is null) continue;
                if (!wellMap.TryGetValue(wellName, out var well)) continue;

                var num = Cell(row.Cell(2).GetString(), 100);
                var top = Depth(row.Cell(3).GetValue<double?>());
                var bot = Depth(row.Cell(4).GetValue<double?>());
                if (num is null || top is null || bot is null) continue;
                if (bot < top) throw new InvalidOperationException("E_IMPORT_RANGE");
                if (!unique.Add((well.Id, num))) continue;

                db.SampleRows.Add(new SampleRow
                {
                    WellId = well.Id,
                    SampleNumber = num,
                    Top = top.Value,
                    Bottom = bot.Value,
                    SampleTypeCode = typeCode,
                    ZoneName = Cell(row.Cell(6).GetString(), 200),
                });
                added++;
            }
        }

        return added;
    }

    static int ImportSrpMulti(XLWorkbook wb, AppDbContext db, Dictionary<string, Well> wellMap, CancellationToken ct)
    {
        var ws = wb.Worksheets.FirstOrDefault(s =>
            s.Name.Contains("SRP", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return 0;

        foreach (var well in wellMap.Values)
            Retire(db.SrpRows.Where(r => r.WellId == well.Id && !r.IsDeleted));

        int added = 0;
        int seen = 0;
        foreach (var row in ws.RowsUsed().Skip(2))
        {
            ct.ThrowIfCancellationRequested();
            if (++seen > MaxRowsPerSheet) throw new InvalidOperationException("E_IMPORT_TOOMANY");

            var wellName = WellName(row.Cell(1).GetString());
            if (wellName is null) continue;
            if (!wellMap.TryGetValue(wellName, out var well)) continue;

            var md = Depth(row.Cell(2).GetValue<double?>());
            var gk = row.Cell(3).GetValue<double?>();
            if (md is null || gk is null || !double.IsFinite(gk.Value)) continue;

            db.SrpRows.Add(new SrpRow { WellId = well.Id, Md = md.Value, CoreGk = gk.Value });
            added++;
        }

        return added;
    }

    static void WriteSamplesWorksheet(XLWorkbook wb, string sheetName, Well well, IReadOnlyList<SampleRow> rows)
    {
        var ws = wb.Worksheets.FirstOrDefault(s => s.Name == sheetName);
        if (ws != null) return;
        ws = wb.AddWorksheet(sheetName);

        string[] head = { "well", "Simple", "top", "bot", "md", "zone name" };
        for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        ws.Cell(2, 3).Value = "m"; ws.Cell(2, 4).Value = "m"; ws.Cell(2, 5).Value = "m";
        ws.Row(1).Style.Font.Bold = true;

        int r = 3;
        int orderNo = 1;
        foreach (var s in rows.Where(x => !x.IsDeleted).OrderBy(x => x.Top))
        {
            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = s.SampleNumber;
            ws.Cell(r, 3).Value = s.Top;
            ws.Cell(r, 4).Value = s.Bottom;
            ws.Cell(r, 5).Value = s.Length;
            ws.Cell(r, 6).Value = orderNo.ToString();
            r++;
            orderNo++;
        }
        ws.Columns().AdjustToContents();
    }

    static void WriteSamplesWorksheetMulti(
        XLWorkbook wb,
        string sheetName,
        IReadOnlyList<(Well well, SampleRow sample)> rows)
    {
        var existing = wb.Worksheets.FirstOrDefault(s => s.Name == sheetName);
        if (existing != null) return;
        var ws = wb.AddWorksheet(sheetName);

        string[] head = { "well", "Simple", "top", "bot", "md", "zone name" };
        for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        ws.Cell(2, 3).Value = "m"; ws.Cell(2, 4).Value = "m"; ws.Cell(2, 5).Value = "m";
        ws.Row(1).Style.Font.Bold = true;

        int r = 3;
        // Zone name (T/R) har bir quduq ichida alohida
        var orderByWell = new Dictionary<int, int>();
        foreach (var (well, s) in rows)
        {
            if (!orderByWell.TryGetValue(well.Id, out var orderNo))
                orderNo = 1;

            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = s.SampleNumber;
            ws.Cell(r, 3).Value = s.Top;
            ws.Cell(r, 4).Value = s.Bottom;
            ws.Cell(r, 5).Value = s.Length;
            ws.Cell(r, 6).Value = orderNo.ToString();
            r++;
            orderByWell[well.Id] = orderNo + 1;
        }
        ws.Columns().AdjustToContents();
    }


    static void Retire(IEnumerable<ITrackedEntity> rows)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var row in rows)
        {
            row.IsDeleted = true;
            row.DeletedUtc = now;
        }
    }

    static double? Depth(double? value)
    {
        if (value is null) return null;
        if (!double.IsFinite(value.Value) || Math.Abs(value.Value) > DepthLimit)
            throw new InvalidOperationException("E_IMPORT_RANGE");
        return value;
    }

    static string? Cell(string? value, int max = MaxTextCell)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > max) trimmed = trimmed[..max];
        var builder = new System.Text.StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            builder.Append(char.IsControl(c) && c != '\n' && c != '\t' ? ' ' : c);
        return builder.ToString();
    }

    static string? WellName(string? value)
    {
        var cleaned = Cell(value, 100);
        return cleaned;
    }

    static int? Code(int? value) =>
        value is int c && c is >= -1_000_000 and <= 1_000_000 ? c : null;

    static string? NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}