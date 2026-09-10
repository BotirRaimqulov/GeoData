using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using GeoDataPro.App.Services;
using GeoDataPro.Core.Services;
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

    /// <summary>Jadvalda ko'p tanlangan qatorlar (Ctrl+Click / Shift+Click orqali).</summary>
    public ObservableCollection<SampleRow> SelectedItems { get; } = new();

    [ObservableProperty] private SampleRow? _selected;
    [ObservableProperty] private int? _selectedSampleTypeCode = 11;
    [ObservableProperty] private int _count;
    [ObservableProperty] private double _totalLength;
    [ObservableProperty] private bool _hasUnsaved;

    public bool CanEdit => _state.Can(Permissions.SampleWrite);

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

    void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Eski qatorlar subscribe dan chiqariladi.
        if (e.OldItems != null)
        {
            foreach (SampleRow row in e.OldItems)
                UnsubscribeRow(row);
        }

        // Yangi qatorlar subscribe qilinadi.
        if (e.NewItems != null)
        {
            foreach (SampleRow row in e.NewItems)
                SubscribeRow(row);
        }

        // Har qanday qo'shish/o'chirish — saqlanmagan holatga o'tadi.
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
            HasUnsaved = true;
    }

    public void Load()
    {
        UnsubscribeAllRows();
        Rows.Clear();
        HasUnsaved = false;
        var well = _state.CurrentWell;
        if (well != null)
        {
            foreach (var r in _state.Data.GetSampleRows(well.Id))
            {
                if (!r.SampleTypeCode.HasValue)
                    r.SampleTypeCode = InferSampleTypeCode(r.SampleNumber, well.Number);
                SubscribeRow(r);
                Rows.Add(r);
            }
        }
        // Add paytida HasUnsaved true bo'lib qoladi — load holatida uni tozalaymiz.
        HasUnsaved = false;
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
        // Ko'p tanlangan qatorlar bo'lsa — hammasini o'chiramiz.
        var toDelete = SelectedItems.Count > 1
            ? SelectedItems.ToList()
            : (Selected != null ? new System.Collections.Generic.List<SampleRow> { Selected } : new System.Collections.Generic.List<SampleRow>());

        if (toDelete.Count == 0) return;

        string msg = toDelete.Count == 1
            ? "Tanlangan namunani o'chirasizmi?"
            : $"Tanlangan {toDelete.Count} ta namunani o'chirasizmi?";

        if (MessageBox.Show(msg, "Tasdiqlash",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SelectedItems.Clear();
        foreach (var r in toDelete)
        {
            UnsubscribeRow(r);
            Rows.Remove(r);
        }
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

        try
        {
            _state.Data.SaveSamples(well.Id, Rows.ToList());
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Namunalarni saqlab bo'lmadi.", ex);
            return;
        }

        Load();
        HasUnsaved = false;
        Recalc();
        _state.RaiseDataChanged();
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

    void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Har qanday maydon o'zgarishi saqlanmagan holatga o'tadi.
        HasUnsaved = true;

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