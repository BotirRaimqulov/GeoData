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
    readonly AppState _state = AppState.Instance;
    public ObservableCollection<SampleRow> Rows { get; } = new();
    [ObservableProperty] private SampleRow? _selected;
    [ObservableProperty] private int _count;
    [ObservableProperty] private double _totalLength;

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
                Rows.Add(r);
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
        long num = (last?.SampleNumber ?? 0) + 1;
        Rows.Add(new SampleRow { WellId = well.Id, SampleNumber = num, Top = top, Bottom = Math.Round(top + 0.5, 2) });
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
