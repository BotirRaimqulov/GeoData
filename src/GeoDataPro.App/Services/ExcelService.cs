using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Services;

/// <summary>Excel import/eksport — "Шаблон.xlsx" formatiga mos.</summary>
public static class ExcelService
{
    // -------- Dala jurnali --------
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
            db.JournalRows.RemoveRange(db.JournalRows.Where(r => r.WellId == wellId));

            int order = 1, added = 0;
            foreach (var row in ws.RowsUsed().Skip(2)) // header + unit row
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
        foreach (var j in rows.OrderBy(x => x.OrderNo))
        {
            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = j.Top;
            ws.Cell(r, 3).Value = j.Bottom;
            ws.Cell(r, 4).Value = j.CoreRecoveryM;
            // ZoneName bo'sh bo'lsa — T/R (OrderNo) bilan to'ldiramiz.
            ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(j.ZoneName) ? j.OrderNo.ToString() : j.ZoneName;
            ws.Cell(r, 6).Value = j.LithoCode;
            ws.Cell(r, 7).Value = j.ColorCode;
            ws.Cell(r, 8).Value = j.Description;
            r++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    // -------- SRP (Core_GK) --------
    public static int ImportSrp(string path, int wellId, string wellNumber)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name.Contains("SRP", StringComparison.OrdinalIgnoreCase)) ?? wb.Worksheet(1);
            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();
            db.SrpRows.RemoveRange(db.SrpRows.Where(r => r.WellId == wellId));
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
        // T/R (tartib raqami) — MD bo'yicha tartiblangan ketma-ket raqam.
        int orderNo = 1;
        foreach (var p in rows.OrderBy(x => x.Md))
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

    // -------- Namuna --------
    public static int ImportSamples(string path, int wellId)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();
            db.SampleRows.RemoveRange(db.SampleRows.Where(r => r.WellId == wellId));

            // Namuna (umumiy) va Namuna_11/Namuna_12/Namuna_0/Namuna_4 worksheetlaridan ham o'qiymiz.
            // Agar bir nechtasi bo'lsa, dublikat SampleNumber larni tashlab ketamiz.
            var sheetNames = new[] { "Namuna", "Namuna_11", "Namuna_12", "Namuna_0", "Namuna_4" };
            var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;

            foreach (var sheetName in sheetNames)
            {
                var ws = wb.Worksheets.FirstOrDefault(s =>
                    s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) continue;

                foreach (var row in ws.RowsUsed().Skip(2))
                {
                    var num = row.Cell(2).GetString().Trim();
                    var top = row.Cell(3).GetValue<double?>();
                    var bot = row.Cell(4).GetValue<double?>();
                    if (string.IsNullOrWhiteSpace(num) || top is null || bot is null) continue;
                    if (!seenNumbers.Add(num)) continue; // dublikatni tashlab ketamiz

                    db.SampleRows.Add(new SampleRow
                    {
                        WellId = wellId, SampleNumber = num, Top = top.Value, Bottom = bot.Value,
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

        // Har bir namuna turi uchun alohida worksheet yaratamiz:
        // Namuna_11 (Oddiy), Namuna_12 (Yalpi), Namuna_0 (Granulametrik), Namuna_4 (Mineralogik)
        var typeGroups = new (int Code, string SheetName)[]
        {
            (11, "Namuna_11"),
            (12, "Namuna_12"),
            (0,  "Namuna_0"),
            (4,  "Namuna_4"),
        };

        // Umumiy "Barcha namunalar" varag'i ham bo'ladi (ixtiyoriy).
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

    static void WriteSamplesWorksheet(XLWorkbook wb, string sheetName, Well well, IReadOnlyList<SampleRow> rows)
    {
        // XLWorkbook bu fayl uchun bir xil nomdagi worksheet'ni qabul qilmaydi — qo'shimcha raqam bilan farqlaymiz.
        var ws = wb.Worksheets.FirstOrDefault(s => s.Name == sheetName);
        if (ws != null) return;
        ws = wb.AddWorksheet(sheetName);

        string[] head = { "well", "Simple", "top", "bot", "md", "zone name" };
        for (int i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        ws.Cell(2, 3).Value = "m"; ws.Cell(2, 4).Value = "m"; ws.Cell(2, 5).Value = "m";
        ws.Row(1).Style.Font.Bold = true;

        int r = 3;
        int orderNo = 1;
        foreach (var s in rows.OrderBy(x => x.Top))
        {
            ws.Cell(r, 1).Value = well.Number;
            ws.Cell(r, 2).Value = s.SampleNumber;
            ws.Cell(r, 3).Value = s.Top;
            ws.Cell(r, 4).Value = s.Bottom;
            ws.Cell(r, 5).Value = s.Length;
            ws.Cell(r, 6).Value = orderNo.ToString(); // Zone name = T/R
            r++;
            orderNo++;
        }
        ws.Columns().AdjustToContents();
    }

    // -------- Full workbook export --------
    public static void ExportWorkbook(string path, Well well)
    {
        using var db = new AppDbContext();
        var journal = db.JournalRows.Where(r => r.WellId == well.Id).OrderBy(r => r.OrderNo).ToList();
        var samples = db.SampleRows.Where(r => r.WellId == well.Id).OrderBy(r => r.Top).ToList();
        var srp = db.SrpRows.Where(r => r.WellId == well.Id).OrderBy(r => r.Md).ToList();

        using var wb = new XLWorkbook();
        // Dala jurnali — zona nomi saqlangan holatda (avto yoki qo'lda yozilgan);
        // agar bazada bo'sh bo'lsa, T/R (OrderNo) bilan to'ldiramiz (vazifa 2 auto-rejim).
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
                // ZoneName bo'sh bo'lsa — T/R (OrderNo) bilan to'ldiramiz.
                ws.Cell(r, 5).Value = string.IsNullOrWhiteSpace(j.ZoneName) ? j.OrderNo.ToString() : j.ZoneName;
                ws.Cell(r, 6).Value = j.LithoCode; ws.Cell(r, 7).Value = j.ColorCode; ws.Cell(r, 8).Value = j.Description;
                r++;
            }
            ws.Columns().AdjustToContents();
        }
        // SRP — zona nomi sifatida T/R (1, 2, 3...) yoziladi.
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
        // Namuna — har bir namuna turi uchun alohida worksheet:
        // Namuna (umumiy), Namuna_11, Namuna_12, Namuna_0, Namuna_4
        // Zone name = T/R (1, 2, 3...) har bir worksheetda o'sha worksheet ichidagi ketma-ket raqam.
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

    static string? NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}