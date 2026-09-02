using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

public partial class SrpViewModel : ObservableObject
{
    readonly AppState _state = AppState.Instance;
    public ObservableCollection<SrpRow> Rows { get; } = new();
    [ObservableProperty] private SrpRow? _selected;

    [ObservableProperty] private int _count;
    [ObservableProperty] private double _minGk;
    [ObservableProperty] private double _maxGk;
    [ObservableProperty] private double _avgGk;

    public ObservableCollection<GkPoint> Chart { get; } = new();

    public SrpViewModel()
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
            foreach (var r in db.SrpRows.Where(s => s.WellId == well.Id).OrderBy(s => s.Md))
                Rows.Add(r);
        }
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
        double spanMd = maxMd - minMd, spanV = Math.Max(maxV - minV, 0.001);
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
        Rows.Add(new SrpRow { WellId = well.Id, Md = md, CoreGk = 0 });
        Recalc();
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
        var existing = db.SrpRows.Where(s => s.WellId == well.Id).ToList();
        var keep = Rows.Where(r => r.Id != 0).Select(r => r.Id).ToHashSet();
        foreach (var g in existing.Where(e => !keep.Contains(e.Id))) db.SrpRows.Remove(g);
        foreach (var r in Rows)
        {
            r.WellId = well.Id;
            if (r.Id == 0) db.SrpRows.Add(r); else db.SrpRows.Update(r);
        }
        db.SaveChanges();
        Recalc();
        MessageBox.Show("SRP ma'lumotlari saqlandi.", "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

public class GkPoint
{
    public double Md { get; set; }
    public double Value { get; set; }
    public double NormX { get; set; }
    public double NormY { get; set; }
}
