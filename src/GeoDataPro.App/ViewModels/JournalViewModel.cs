using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoDataPro.App.ViewModels;

public partial class JournalViewModel : ObservableObject
{
    readonly AppState _state = AppState.Instance;
    readonly RefCache _ref = RefCache.Instance;

    public ObservableCollection<JournalRowVm> Rows { get; } = new();

    [ObservableProperty] private JournalRowVm? _selected;
    [ObservableProperty] private bool _hasUnsaved;

    public RefCache Ref => _ref;

    /// <summary>Tavsif maydoni uchun autocomplete manbai: shablonlar + shu quduqda ishlatilgan tavsiflar.</summary>
    public ObservableCollection<string> DescriptionSuggestions { get; } = new();

    public JournalViewModel()
    {
        _state.WellChanged += Load;
        _state.DataChanged += RebuildSuggestions;
        Load();
    }

    void RebuildSuggestions()
    {
        var set = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var t in _ref.Descriptions)
            if (!string.IsNullOrWhiteSpace(t.Text)) set.Add(t.Text.Trim());
        foreach (var r in Rows)
            if (!string.IsNullOrWhiteSpace(r.Description)) set.Add(r.Description!.Trim());

        DescriptionSuggestions.Clear();
        foreach (var s in set) DescriptionSuggestions.Add(s);
    }

    partial void OnSelectedChanged(JournalRowVm? value) => OnPropertyChanged(nameof(Selected));

    // ---------- Load / Save ----------
    public void Load()
    {
        Rows.Clear();
        HasUnsaved = false;
        var well = _state.CurrentWell;
        if (well == null) { Recalc(); return; }

        using var db = new AppDbContext();
        var rows = db.JournalRows.AsNoTracking()
                                 .Where(r => r.WellId == well.Id)
                                 .OrderBy(r => r.OrderNo).ThenBy(r => r.Top).ToList();
        foreach (var r in rows)
        {
            var vm = new JournalRowVm(r);
            vm.DirtyChanged += () => { HasUnsaved = true; Recalc(); };
            Rows.Add(vm);
        }
        Selected = Rows.FirstOrDefault();
        RebuildSuggestions();
        Recalc();
    }

    [RelayCommand]
    public void Save()
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
            var existingById = db.JournalRows.Where(r => r.WellId == well.Id).ToDictionary(r => r.Id);
            var keepIds = Rows.Where(r => r.Model.Id != 0).Select(r => r.Model.Id).ToHashSet();

            foreach (var gone in existingById.Values.Where(e => !keepIds.Contains(e.Id)))
                db.JournalRows.Remove(gone);

            int order = 1;
            foreach (var vm in Rows)
            {
                var m = vm.Model;
                m.ZoneName = string.IsNullOrWhiteSpace(m.ZoneName) ? null : m.ZoneName.Trim();
                m.OrderNo = order++;
                m.WellId = well.Id;
                if (m.Id == 0) db.JournalRows.Add(m);
                else if (existingById.TryGetValue(m.Id, out var tracked)) db.Entry(tracked).CurrentValues.SetValues(m);
                else db.JournalRows.Update(m);
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Dala jurnalini saqlab bo'lmadi.", ex);
            return;
        }

        foreach (var vm in Rows) vm.ClearDirty();
        HasUnsaved = false;
        _state.RaiseDataChanged();
    }

    // ---------- Row ops ----------
    [RelayCommand]
    public void AddRow()
    {
        var last = Rows.LastOrDefault();
        double top = last?.Bottom ?? _state.CurrentWell?.StartDepth ?? 0;
        var m = new JournalRow { Top = top, Bottom = Math.Round(top + 1, 2), CoreRecoveryM = 0, OrderNo = Rows.Count + 1 };
        var vm = new JournalRowVm(m);
        vm.DirtyChanged += () => { HasUnsaved = true; Recalc(); };
        Rows.Add(vm);
        Selected = vm;
        vm.MarkNew();
        Recalc();
    }

    [RelayCommand]
    public void DuplicateRow()
    {
        if (Selected == null) return;
        var s = Selected.Model;
        var m = new JournalRow
        {
            Top = s.Bottom, Bottom = Math.Round(s.Bottom + s.Interval, 2),
            CoreRecoveryM = s.CoreRecoveryM, ZoneName = s.ZoneName,
            LithoCode = s.LithoCode, ColorCode = s.ColorCode, TextureCode = s.TextureCode,
            MineralCode = s.MineralCode, GrainSize = s.GrainSize,
            Description = s.Description,
        };
        var vm = new JournalRowVm(m);
        vm.DirtyChanged += () => { HasUnsaved = true; Recalc(); };
        int idx = Rows.IndexOf(Selected) + 1;
        Rows.Insert(idx, vm);
        Selected = vm;
        vm.MarkNew();
        Recalc();
    }

    [RelayCommand]
    public void DeleteRow()
    {
        if (Selected == null) return;
        if (MessageBox.Show("Tanlangan qatorni o'chirasizmi?", "Tasdiqlash",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        int idx = Rows.IndexOf(Selected);
        Rows.Remove(Selected);
        Selected = Rows.ElementAtOrDefault(Math.Min(idx, Rows.Count - 1));
        HasUnsaved = true;
        Recalc();
    }

    [RelayCommand]
    public void RegenerateDescription()
    {
        Selected?.RegenerateDescription();
    }

    [RelayCommand]
    public void MoveUp()
    {
        if (Selected == null) return;
        int i = Rows.IndexOf(Selected);
        if (i <= 0) return;
        Rows.Move(i, i - 1);
        HasUnsaved = true;
    }

    [RelayCommand]
    public void MoveDown()
    {
        if (Selected == null) return;
        int i = Rows.IndexOf(Selected);
        if (i < 0 || i >= Rows.Count - 1) return;
        Rows.Move(i, i + 1);
        HasUnsaved = true;
    }

    // ---------- Summary ----------
    [ObservableProperty] private int _totalRows;
    [ObservableProperty] private double _totalThickness;
    [ObservableProperty] private double _totalCore;
    [ObservableProperty] private double _avgRecovery;
    [ObservableProperty] private double _depthFrom;
    [ObservableProperty] private double _depthTo;

    public ObservableCollection<ProfileSegment> Profile { get; } = new();

    void Recalc()
    {
        TotalRows = Rows.Count;
        TotalThickness = Math.Round(Rows.Sum(r => r.Interval), 2);
        TotalCore = Math.Round(Rows.Sum(r => r.CoreRecoveryM), 2);
        AvgRecovery = TotalThickness > 0 ? Math.Round(TotalCore / TotalThickness * 100, 1) : 0;
        DepthFrom = Rows.Count > 0 ? Rows.Min(r => r.Top) : 0;
        DepthTo = Rows.Count > 0 ? Rows.Max(r => r.Bottom) : 0;

        Profile.Clear();
        double span = DepthTo - DepthFrom;
        if (span <= 0) return;
        foreach (var r in Rows.OrderBy(r => r.Top))
        {
            Profile.Add(new ProfileSegment
            {
                Label = r.OrderNo.ToString(),
                Weight = Math.Max(r.Interval, 0.01),
                Hex = r.ColorHex,
            });
        }
        OnPropertyChanged(nameof(Profile));
    }

    bool ValidateRows(out string message)
    {
        JournalRowVm? previous = null;
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.Bottom <= row.Top)
            {
                message = $"{i + 1}-qatorda pastki chuqurlik yuqori chuqurlikdan katta bo'lishi kerak.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.ZoneName))
            {
                message = $"{i + 1}-qatorda zona nomini kiriting.";
                return false;
            }

            if (previous != null && row.Top < previous.Bottom)
            {
                message = "Dala jurnali qatorlari chuqurlik bo'yicha tartibli va kesishmasdan bo'lishi kerak.";
                return false;
            }

            previous = row;
        }

        message = string.Empty;
        return true;
    }
}

public class ProfileSegment
{
    public string Label { get; set; } = "";
    public double Weight { get; set; }
    public string Hex { get; set; } = "#CCCCCC";
}
