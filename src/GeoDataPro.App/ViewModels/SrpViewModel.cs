using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.App.ViewModels;

public partial class SrpViewModel : ObservableObject
{
    readonly AppState _state = AppState.Instance;
    public ObservableCollection<SrpRow> Rows { get; } = new();

    /// <summary>Jadvalda ko'p tanlangan qatorlar (Ctrl+Click / Shift+Click orqali).</summary>
    public ObservableCollection<SrpRow> SelectedItems { get; } = new();

    [ObservableProperty] private SrpRow? _selected;
    [ObservableProperty] private bool _hasUnsaved;

    [ObservableProperty] private int _count;
    [ObservableProperty] private double _minGk;
    [ObservableProperty] private double _maxGk;
    [ObservableProperty] private double _avgGk;

    public ObservableCollection<GkPoint> Chart { get; } = new();

    public SrpViewModel()
    {
        _state.WellChanged += Load;
        Rows.CollectionChanged += Rows_CollectionChanged;
        Load();
    }

    void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
            HasUnsaved = true;
    }

    void SubscribeRow(SrpRow row) => row.PropertyChanged += Row_PropertyChanged;
    void UnsubscribeRow(SrpRow row) => row.PropertyChanged -= Row_PropertyChanged;
    void UnsubscribeAllRows()
    {
        foreach (var row in Rows) UnsubscribeRow(row);
    }

    void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e) => HasUnsaved = true;

    public void Load()
    {
        UnsubscribeAllRows();
        Rows.Clear();
        HasUnsaved = false;
        var well = _state.CurrentWell;
        if (well != null)
        {
            using var db = new AppDbContext();
            foreach (var r in db.SrpRows.AsNoTracking().Where(s => s.WellId == well.Id).OrderBy(s => s.Md))
            {
                SubscribeRow(r);
                Rows.Add(r);
            }
        }
        // Add paytida HasUnsaved true bo'lib qoladi — load holatida uni tozalaymiz.
        HasUnsaved = false;
        Selected = Rows.FirstOrDefault();
        Recalc();
    }

    void Recalc()
    {
        Count = Rows.Count;
        MinGk = Rows.Count > 0 ? Math.Round(Rows.Min(r => r.CoreGk), 1) : 0;
        MaxGk = Rows.Count > 0 ? Math.Round(Rows.Max(r => r.CoreGk), 1) : 0;
        AvgGk = Rows.Count > 0 ? Math.Round(Rows.Average(r => r.CoreGk), 1) : 0;

        Chart.Clear();
        if (Rows.Count < 2) { OnPropertyChanged(nameof(Chart)); return; }
        double minMd = Rows.Min(r => r.Md), maxMd = Rows.Max(r => r.Md);
        double minV = Rows.Min(r => r.CoreGk), maxV = Rows.Max(r => r.CoreGk);
        double spanMd = maxMd - minMd;
        double spanV = maxV - minV;
        if (!double.IsFinite(spanMd) || spanMd <= 0) spanMd = 1;
        if (!double.IsFinite(spanV) || spanV <= 0) spanV = 1;
        foreach (var r in Rows.OrderBy(r => r.Md))
            Chart.Add(new GkPoint
            {
                Md = r.Md, Value = r.CoreGk,
                NormY = (r.Md - minMd) / spanMd,
                NormX = (r.CoreGk - minV) / spanV,
            });
        OnPropertyChanged(nameof(Chart));
    }

    [RelayCommand]
    void Add()
    {
        var well = _state.CurrentWell;
        if (well == null) return;
        var last = Rows.LastOrDefault();
        double md = last != null ? Math.Round(last.Md + 0.1, 1) : well.StartDepth ?? 0;
        var row = new SrpRow { WellId = well.Id, Md = md, CoreGk = 0 };
        SubscribeRow(row);
        Rows.Add(row);
        Recalc();
    }

    [RelayCommand]
    void Delete()
    {
        var toDelete = SelectedItems.Count > 1
            ? SelectedItems.ToList()
            : (Selected != null ? new System.Collections.Generic.List<SrpRow> { Selected } : new System.Collections.Generic.List<SrpRow>());

        if (toDelete.Count == 0) return;

        string msg = toDelete.Count == 1
            ? "Tanlangan nuqtani o'chirasizmi?"
            : $"Tanlangan {toDelete.Count} ta nuqtani o'chirasizmi?";

        if (MessageBox.Show(msg, "Tasdiqlash",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SelectedItems.Clear();
        foreach (var r in toDelete)
        {
            UnsubscribeRow(r);
            Rows.Remove(r);
        }
        Recalc();
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
            var existingById = db.SrpRows.Where(s => s.WellId == well.Id).ToDictionary(s => s.Id);
            var keep = Rows.Where(r => r.Id != 0).Select(r => r.Id).ToHashSet();
            foreach (var g in existingById.Values.Where(e => !keep.Contains(e.Id))) db.SrpRows.Remove(g);
            foreach (var r in Rows)
            {
                r.WellId = well.Id;
                if (r.Id == 0) db.SrpRows.Add(r);
                else if (existingById.TryGetValue(r.Id, out var tracked)) db.Entry(tracked).CurrentValues.SetValues(r);
                else db.SrpRows.Update(r);
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("SRP ma'lumotlarini saqlab bo'lmadi.", ex);
            return;
        }

        UnsubscribeAllRows();
        foreach (var r in Rows) SubscribeRow(r);
        HasUnsaved = false;
        Recalc();
        _state.RaiseDataChanged();
        AppNotifier.Info("SRP ma'lumotlari saqlandi.");
    }

    bool ValidateRows(out string message)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (!double.IsFinite(row.Md) || !double.IsFinite(row.CoreGk))
            {
                message = $"{i + 1}-qatorda faqat haqiqiy son qiymatlarini kiriting.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }
}

public class GkPoint
{
    public double Md { get; set; }
    public double Value { get; set; }
    public double NormX { get; set; }
    public double NormY { get; set; }
}