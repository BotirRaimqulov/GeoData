using System;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;
using Microsoft.Win32;

namespace GeoDataPro.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AppState State => AppState.Instance;

    // Section VMs (lazy-ish, created once)
    public JournalViewModel Journal { get; } = new();
    public SamplesViewModel Samples { get; } = new();
    public SrpViewModel Srp { get; } = new();
    public WellsViewModel Wells { get; } = new();

    public ReferenceViewModel LithoRef { get; } = new(ReferenceViewModel.Kind.Litho);
    public ReferenceViewModel ColorRef { get; } = new(ReferenceViewModel.Kind.Color);
    public ReferenceViewModel TextureRef { get; } = new(ReferenceViewModel.Kind.Texture);
    public ReferenceViewModel MineralRef { get; } = new(ReferenceViewModel.Kind.Mineral);
    public ReferenceViewModel DescriptionRef { get; } = new(ReferenceViewModel.Kind.Description);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    private string _currentSection = "journal";

    public string SectionTitle => CurrentSection switch
    {
        "journal" => "Dala jurnali",
        "samples" => "Namuna",
        "srp" => "SRP — Kern GK",
        "litho" => "Litologik kodlar",
        "colors" => "Kern ranglari",
        "textures" => "Teksturalar",
        "minerals" => "Mineralizatsiya",
        "descriptions" => "Tavsif shablonlari",
        "wells" => "Loyiha va quduq boshqaruvi",
        "io" => "Import / Eksport",
        _ => "GeoData Pro",
    };
    [ObservableProperty] private string _statusText = "Barcha o'zgarishlar saqlangan";
    [ObservableProperty] private string _clock = DateTime.Now.ToString("HH:mm:ss");

    public string DbLabel => "Baza: Lokal";
    public string Version => "v1.0.0";

    public MainViewModel()
    {
        RefCache.Instance.Reload();
        State.Reload();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm:ss");
        timer.Start();

        State.DataChanged += () => StatusText = "Barcha o'zgarishlar saqlangan";
        Journal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JournalViewModel.HasUnsaved))
                StatusText = Journal.HasUnsaved ? "Saqlanmagan o'zgarishlar bor" : "Barcha o'zgarishlar saqlangan";
        };
    }

    [RelayCommand]
    void Navigate(string section) => CurrentSection = section;

    // ---------------- Quduq tez qo'shish (Dala jurnali, Namuna, SRP) ----------------
    [RelayCommand]
    void AddWell()
    {
        var project = State.CurrentProject;
        if (project == null) { Warn("Avval loyiha tanlang."); return; }
        var name = Views.PromptDialog.Ask("Yangi quduq nomi:", "Quduq qo'shish", "0000");
        if (string.IsNullOrWhiteSpace(name)) return;
        using var db = new AppDbContext();
        var well = new Well { ProjectId = project.Id, Number = name.Trim() };
        try
        {
            db.Wells.Add(well);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Quduqni saqlab bo'lmadi.", ex);
            return;
        }

        State.Reload(project.Id, well.Id);
        Wells.Load();
    }

    // ---------------- Import / Export ----------------
    [RelayCommand]
    void ImportExcel()
    {
        var well = State.CurrentWell;
        if (well == null) { Warn("Avval quduq tanlang."); return; }
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Excel import" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int j = 0, s = 0, k = 0;
            try { j = ExcelService.ImportJournal(dlg.FileName, well.Id); } catch (Exception ex) { AppNotifier.Error(ex.Message, ex.InnerException ?? ex); }
            try { s = ExcelService.ImportSamples(dlg.FileName, well.Id); } catch (Exception ex) { AppNotifier.Error(ex.Message, ex.InnerException ?? ex); }
            try { k = ExcelService.ImportSrp(dlg.FileName, well.Id, well.Number); } catch (Exception ex) { AppNotifier.Error(ex.Message, ex.InnerException ?? ex); }
            Journal.Load(); Samples.Load(); Srp.Load();
            MessageBox.Show($"Import tugadi:\n  Dala jurnali: {j} qator\n  Namuna: {s} qator\n  SRP: {k} nuqta",
                "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Warn("Import xatosi: " + ex.Message); }
    }

    [RelayCommand]
    void ExportExcel()
    {
        var well = State.CurrentWell;
        if (well == null) { Warn("Avval quduq tanlang."); return; }
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"{State.CurrentProject?.Name}_{well.Number}.xlsx",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ExcelService.ExportWorkbook(dlg.FileName, well);
            MessageBox.Show("Eksport tayyor:\n" + dlg.FileName, "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Warn("Eksport xatosi: " + ex.Message); }
    }

    static void Warn(string msg) => MessageBox.Show(msg, "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
}
