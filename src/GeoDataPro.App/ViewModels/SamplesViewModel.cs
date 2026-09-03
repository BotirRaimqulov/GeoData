using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.App.ViewModels;

public partial class SamplesViewModel : ObservableObject
{
    public sealed class SampleTypeOption
    {
        public int? Code { get; init; }
        public string Name { get; init; } = "";
    }

    readonly AppState _state = AppState.Instance;
    static readonly SampleTypeOption[] _sampleTypeDefaults =
    {
        new() { Code = null, Name = "Barcha namunalar" },
        new() { Code = 11, Name = "Oddiy namuna" },
        new() { Code = 12, Name = "Yalpi namuna" },
        new() { Code = 0, Name = "Granulametrik tarkib namunasi" },
        new() { Code = 4, Name = "Mineralogik namuna" },
    };

    public ObservableCollection<SampleRow> Rows { get; } = new();
    public ICollectionView FilteredRows { get; }
    public ObservableCollection<SampleTypeOption> SampleTypes { get; } = new(_sampleTypeDefaults);
    [ObservableProperty] private SampleRow? _selected;
    [ObservableProperty] private int? _selectedSampleTypeCode = 11;
    [ObservableProperty] private int _count;
    [ObservableProperty] private double _totalLength;

    partial void OnSelectedSampleTypeCodeChanged(int? value)
    {
        RefreshFilter();
    }

    public SamplesViewModel()
    {
        FilteredRows = CollectionViewSource.GetDefaultView(Rows);
        FilteredRows.Filter = FilterRow;
        Rows.CollectionChanged += Rows_CollectionChanged;
        _state.WellChanged += Load;
        Load();
    }

    public void Load()
    {
        UnsubscribeAllRows();
        Rows.Clear();
        var well = _state.CurrentWell;
        if (well != null)
        {
            using var db = new AppDbContext();
            foreach (var r in db.SampleRows.AsNoTracking().Where(s => s.WellId == well.Id).OrderBy(s => s.Top))
            {
                if (!r.SampleTypeCode.HasValue)
                    r.SampleTypeCode = InferSampleTypeCode(r.SampleNumber, well.Number);
                SubscribeRow(r);
                Rows.Add(r);
            }
        }
        RefreshFilter();
    }

    void Recalc()
    {
        var visibleRows = FilteredRows.Cast<SampleRow>().ToList();
        Count = visibleRows.Count;
        TotalLength = Math.Round(visibleRows.Sum(r => r.Length), 2);
    }

    [RelayCommand]
    void Add()
    {
        var well = _state.CurrentWell;
        if (well == null) return;
        var last = Rows.OrderBy(r => r.Top).LastOrDefault();
        double top = last?.Bottom ?? well.StartDepth ?? 0;
        int sampleTypeCode = SelectedSampleTypeCode ?? 11;
        int sequence = NextSequenceForType(well.Number, sampleTypeCode);
        string sampleNumber = $"{sampleTypeCode}{well.Number}{sequence:00}";
        var row = new SampleRow
        {
            WellId = well.Id,
            SampleTypeCode = sampleTypeCode,
            SampleNumber = sampleNumber,
            Top = top,
            Bottom = Math.Round(top + 0.5, 2),
        };
        SubscribeRow(row);
        Rows.Add(row);
        RefreshFilter(selectRow: row);
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
            if (item.Code.HasValue && normalized.StartsWith($"{item.Code.Value}{wellNumber}", StringComparison.OrdinalIgnoreCase))
                return item.Code.Value;
        }
        return 11;
    }

    [RelayCommand]
    void Delete()
    {
        if (Selected == null) return;
        UnsubscribeRow(Selected);
        Rows.Remove(Selected);
        RefreshFilter();
    }

    [RelayCommand]
    void Save()
    {
        var well = _state.CurrentWell;
        if (well == null) return;

        if (!ValidateRows(out var validationError))
        {
            AppNotifier.Warn(validationError);
            return;
        }

        using var db = new AppDbContext();
        try
        {
            var existingById = db.SampleRows.Where(s => s.WellId == well.Id).ToDictionary(s => s.Id);
            var keep = Rows.Where(r => r.Id != 0).Select(r => r.Id).ToHashSet();
            foreach (var g in existingById.Values.Where(e => !keep.Contains(e.Id))) db.SampleRows.Remove(g);
            foreach (var r in Rows)
            {
                r.WellId = well.Id;
                r.SampleNumber = r.SampleNumber.Trim();
                if (r.Id == 0) db.SampleRows.Add(r);
                else if (existingById.TryGetValue(r.Id, out var tracked)) db.Entry(tracked).CurrentValues.SetValues(r);
                else db.SampleRows.Update(r);
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Namunalarni saqlab bo'lmadi.", ex);
            return;
        }

        Recalc();
        AppNotifier.Info("Namunalar saqlandi.");
    }

    bool FilterRow(object item)
    {
        if (item is not SampleRow row)
            return false;

        if (!SelectedSampleTypeCode.HasValue)
            return true;

        return MatchesSampleTypePrefix(row.SampleNumber, SelectedSampleTypeCode.Value);
    }

    static bool MatchesSampleTypePrefix(string? sampleNumber, int sampleTypeCode)
    {
        if (string.IsNullOrWhiteSpace(sampleNumber))
            return false;

        return sampleNumber.Trim().StartsWith(sampleTypeCode.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    void RefreshFilter(SampleRow? selectRow = null)
    {
        FilteredRows.Refresh();
        ReindexVisibleRows();

        var preferred = selectRow != null && FilterRow(selectRow) ? selectRow : Selected;
        if (preferred == null || !FilterRow(preferred) || !FilteredRows.Cast<SampleRow>().Contains(preferred))
            preferred = FilteredRows.Cast<SampleRow>().FirstOrDefault();

        Selected = preferred;
        Recalc();
    }

    void ReindexVisibleRows()
    {
        foreach (var row in Rows)
            row.DisplayOrder = 0;

        int index = 1;
        foreach (var row in FilteredRows.Cast<SampleRow>())
            row.DisplayOrder = index++;
    }

    void SubscribeRow(SampleRow row)
    {
        row.PropertyChanged -= Row_PropertyChanged;
        row.PropertyChanged += Row_PropertyChanged;
    }

    void UnsubscribeRow(SampleRow row)
    {
        row.PropertyChanged -= Row_PropertyChanged;
    }

    void UnsubscribeAllRows()
    {
        foreach (var row in Rows)
            UnsubscribeRow(row);
    }

    void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (SampleRow row in e.OldItems)
                UnsubscribeRow(row);
        }

        if (e.NewItems != null)
        {
            foreach (SampleRow row in e.NewItems)
                SubscribeRow(row);
        }
    }

    void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SampleRow.Top) or nameof(SampleRow.Bottom))
        {
            Recalc();
            return;
        }

        if (e.PropertyName is nameof(SampleRow.SampleNumber) or nameof(SampleRow.SampleTypeCode))
        {
            if (sender is SampleRow row && !row.SampleTypeCode.HasValue)
                row.SampleTypeCode = InferSampleTypeCode(row.SampleNumber, _state.CurrentWell?.Number ?? "");

            RefreshFilter();
        }
    }

    bool ValidateRows(out string message)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (string.IsNullOrWhiteSpace(row.SampleNumber))
            {
                message = $"{i + 1}-qatorda namuna raqamini kiriting.";
                return false;
            }

            var sampleNumber = row.SampleNumber.Trim();
            if (!seen.Add(sampleNumber))
            {
                message = $"'{sampleNumber}' namuna raqami takrorlangan.";
                return false;
            }

            if (row.Bottom <= row.Top)
            {
                message = $"{i + 1}-qatorda pastki chuqurlik yuqori chuqurlikdan katta bo'lishi kerak.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }
}
