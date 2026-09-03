using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

public partial class SamplesViewModel : ObservableObject
{
    public sealed class SampleTypeOption
    {
        public int Code { get; init; }
        public string Name { get; init; } = "";
    }

    readonly AppState _state = AppState.Instance;
    static readonly SampleTypeOption[] _sampleTypeDefaults =
    {
        new() { Code = 11, Name = "Oddiy namuna" },
        new() { Code = 12, Name = "Yalpi namuna" },
        new() { Code = 0, Name = "Granulametrik tarkib namunasi" },
        new() { Code = 4, Name = "Mineralogik namuna" },
    };

    public ObservableCollection<SampleRow> Rows { get; } = new();
    public ObservableCollection<SampleTypeOption> SampleTypes { get; } = new(_sampleTypeDefaults);
    [ObservableProperty] private SampleRow? _selected;
    [ObservableProperty] private int _selectedSampleTypeCode = 11;
    [ObservableProperty] private int _count;
    [ObservableProperty] private double _totalLength;

    partial void OnSelectedChanged(SampleRow? value)
    {
        if (value?.SampleTypeCode is int code)
            SelectedSampleTypeCode = code;
    }

    public SamplesViewModel()
    {
        _state.WellChanged += Load;
        Load();
    }

    public void Load()
    {
        Rows.Clear();
        var well = _state.CurrentWell;
        if (well != null)
        {
            using var db = new AppDbContext();
            foreach (var r in db.SampleRows.Where(s => s.WellId == well.Id).OrderBy(s => s.Top))
            {
                if (!r.SampleTypeCode.HasValue)
                    r.SampleTypeCode = InferSampleTypeCode(r.SampleNumber, well.Number);
                Rows.Add(r);
            }
        }
        Selected = Rows.FirstOrDefault();
        Recalc();
    }

    void Recalc()
    {
        Count = Rows.Count;
        TotalLength = Math.Round(Rows.Sum(r => r.Length), 2);
    }

    [RelayCommand]
    void Add()
    {
        var well = _state.CurrentWell;
        if (well == null) return;
        var last = Rows.LastOrDefault();
        double top = last?.Bottom ?? well.StartDepth ?? 0;
        int sampleTypeCode = SelectedSampleTypeCode;
        int sequence = NextSequenceForType(well.Number, sampleTypeCode);
        string sampleNumber = $"{sampleTypeCode}{well.Number}{sequence:00}";
        Rows.Add(new SampleRow
        {
            WellId = well.Id,
            SampleTypeCode = sampleTypeCode,
            SampleNumber = sampleNumber,
            Top = top,
            Bottom = Math.Round(top + 0.5, 2),
        });
        Recalc();
    }

    int NextSequenceForType(string wellNumber, int sampleTypeCode)
    {
        var prefix = $"{sampleTypeCode}{wellNumber}";
        var max = Rows.Select(r => ParseSequence(r.SampleNumber, prefix))
                      .Where(v => v.HasValue)
                      .Select(v => v!.Value)
                      .DefaultIfEmpty(0)
                      .Max();
        return max + 1;
    }

    static int? ParseSequence(string? sampleNumber, string prefix)
    {
        if (string.IsNullOrWhiteSpace(sampleNumber)) return null;
        var value = sampleNumber.Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var tail = value.Substring(prefix.Length);
        return int.TryParse(tail, out var seq) ? seq : null;
    }

    static int InferSampleTypeCode(string? sampleNumber, string wellNumber)
    {
        if (string.IsNullOrWhiteSpace(sampleNumber))
            return 11;

        var normalized = sampleNumber.Trim();
        foreach (var item in _sampleTypeDefaults)
        {
            if (normalized.StartsWith($"{item.Code}{wellNumber}", StringComparison.OrdinalIgnoreCase))
                return item.Code;
        }
        return 11;
    }

    [RelayCommand]
    void Delete()
    {
        if (Selected == null) return;
        Rows.Remove(Selected);
        Recalc();
    }

    [RelayCommand]
    void Save()
    {
        var well = _state.CurrentWell;
        if (well == null) return;
        using var db = new AppDbContext();
        var existing = db.SampleRows.Where(s => s.WellId == well.Id).ToList();
        var keep = Rows.Where(r => r.Id != 0).Select(r => r.Id).ToHashSet();
        foreach (var g in existing.Where(e => !keep.Contains(e.Id))) db.SampleRows.Remove(g);
        foreach (var r in Rows)
        {
            r.WellId = well.Id;
            if (r.Id == 0) db.SampleRows.Add(r); else db.SampleRows.Update(r);
        }
        db.SaveChanges();
        Recalc();
        MessageBox.Show("Namunalar saqlandi.", "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
