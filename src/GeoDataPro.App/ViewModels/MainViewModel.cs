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

        // State.DataChanged — saqlash tugagandan keyin global holatni "yangilangan" deb belgilaydi.
        State.DataChanged += () => StatusText = "Barcha o'zgarishlar saqlangan";

        // Har bir bo'limning HasUnsaved o'zgarishini kuzatamiz va global status ni yangilaymiz.
        SubscribeUnsaved(Journal, nameof(JournalViewModel.HasUnsaved));
        SubscribeUnsaved(Samples, nameof(SamplesViewModel.HasUnsaved));
        SubscribeUnsaved(Srp, nameof(SrpViewModel.HasUnsaved));
        SubscribeUnsaved(Wells, nameof(WellsViewModel.HasUnsaved));
        SubscribeUnsaved(LithoRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(ColorRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(TextureRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(MineralRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(DescriptionRef, nameof(ReferenceViewModel.HasUnsaved));
    }

    void SubscribeUnsaved(ObservableObject vm, string hasUnsavedProp)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == hasUnsavedProp)
                RefreshStatusText();
        };
    }

    void RefreshStatusText()
    {
        bool anyUnsaved = Journal.HasUnsaved || Samples.HasUnsaved || Srp.HasUnsaved
                          || Wells.HasUnsaved
                          || LithoRef.HasUnsaved || ColorRef.HasUnsaved || TextureRef.HasUnsaved
                          || MineralRef.HasUnsaved || DescriptionRef.HasUnsaved;
        StatusText = anyUnsaved ? "Saqlanmagan o'zgarishlar bor" : "Barcha o'zgarishlar saqlangan";
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

    // ---------------- Import / Export (multi-well + loyiha) ----------------

    /// <summary>
    /// Excel import: barcha quduqlar uchun.
    /// - Mavjud loyihaga qo'shish yoki yangi loyiha yaratish mumkin.
    /// - Excel dagi Well Name bo'yicha quduq topilmasa — avtomatik yaratiladi.
    /// </summary>
    [RelayCommand]
    void ImportExcel()
    {
        var dlg = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", Title = "Excel import (barcha quduqlar)" };
        if (dlg.ShowDialog() != true) return;

        // Loyiha tanlash / yangi yaratish
        var projectChoice = AskProjectForImport();
        if (projectChoice == null) return; // bekor qilindi

        try
        {
            ExcelService.ImportResult result;
            if (projectChoice.Value.createNew)
            {
                result = ExcelService.ImportWorkbookToProject(
                    dlg.FileName,
                    projectId: null,
                    newProjectName: projectChoice.Value.name);
            }
            else
            {
                result = ExcelService.ImportWorkbook(dlg.FileName, projectChoice.Value.projectId);
            }

            // Holatni yangilash — yangi/yangilangan loyihani topamiz
            State.Reload();
            var targetProject = State.Projects.FirstOrDefault(p =>
                string.Equals(p.Name, result.ProjectName, StringComparison.OrdinalIgnoreCase));
            if (targetProject != null)
                State.Reload(targetProject.Id);

            Wells.Load();
            Journal.Load();
            Samples.Load();
            Srp.Load();

            var wellsList = string.Join(", ", result.WellNumbers.Take(15));
            if (result.WellNumbers.Count > 15)
                wellsList += $" ... (+{result.WellNumbers.Count - 15})";

            MessageBox.Show(
                "Import tugadi:\n" +
                $"  Loyiha: {result.ProjectName}\n" +
                $"  Quduqlar: {result.WellNumbers.Count} ta " +
                $"(yangi: {result.WellsCreated}, mavjud: {result.WellsUpdated})\n" +
                $"  → {wellsList}\n\n" +
                $"  Dala jurnali: {result.JournalRows} qator\n" +
                $"  Namuna: {result.SampleRows} qator\n" +
                $"  SRP: {result.SrpRows} nuqta",
                "GeoData Pro — Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Import xatosi: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Excel eksport: joriy loyihadagi BARCHA quduqlar (yoki faqat joriy quduq — tanlov).
    /// </summary>
    [RelayCommand]
    void ExportExcel()
    {
        var project = State.CurrentProject;
        if (project == null) { Warn("Avval loyiha tanlang."); return; }

        // Tanlov: barcha quduqlar yoki faqat joriy
        var choice = MessageBox.Show(
            $"Loyiha: {project.Name}\n\n" +
            "Barcha quduqlarni eksport qilishni xohlaysizmi?\n\n" +
            "  Ha  — loyihadagi barcha quduqlar\n" +
            "  Yo'q — faqat joriy tanlangan quduq",
            "Eksport rejimi",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel) return;

        bool exportAll = choice == MessageBoxResult.Yes;

        if (!exportAll && State.CurrentWell == null)
        {
            Warn("Joriy quduq tanlanmagan. Avval quduq tanlang yoki 'Ha' ni bosing.");
            return;
        }

        var defaultName = exportAll
            ? $"{project.Name}_barcha_quduqlar.xlsx"
            : $"{project.Name}_{State.CurrentWell!.Number}.xlsx";

        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = defaultName,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (exportAll)
            {
                ExcelService.ExportWorkbookMulti(dlg.FileName, project.Id);
            }
            else
            {
                ExcelService.ExportWorkbook(dlg.FileName, State.CurrentWell!);
            }

            MessageBox.Show(
                "Eksport tayyor:\n" + dlg.FileName +
                (exportAll ? "\n\n(Loyihadagi barcha quduqlar)" : $"\n\n(Faqat: {State.CurrentWell!.Number})"),
                "GeoData Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Warn("Eksport xatosi: " + ex.Message);
        }
    }

    /// <summary>
    /// Import uchun loyiha tanlash dialogi.
    /// Returns (createNew, projectId, name) yoki null (bekor).
    /// </summary>
    static (bool createNew, int projectId, string name)? AskProjectForImport()
    {
        var state = AppState.Instance;
        var projects = state.Projects;

        // Agar loyihalar yo'q bo'lsa — majburan yangi yaratish
        if (projects.Count == 0)
        {
            var name = Views.PromptDialog.Ask(
                "Loyiha yo'q. Yangi loyiha nomini kiriting:",
                "Yangi loyiha (import)",
                "Loyiha-import");
            if (string.IsNullOrWhiteSpace(name)) return null;
            return (true, 0, name.Trim());
        }

        // Tanlov: mavjud yoki yangi
        var mode = MessageBox.Show(
            "Import qaysi loyihaga yozilsin?\n\n" +
            "  Ha  — joriy / mavjud loyihaga qo'shish\n" +
            "  Yo'q — yangi loyiha yaratish\n" +
            "  Bekor — importni to'xtatish",
            "Loyiha tanlash",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (mode == MessageBoxResult.Cancel) return null;

        if (mode == MessageBoxResult.No)
        {
            // Yangi loyiha
            var name = Views.PromptDialog.Ask(
                "Yangi loyiha nomi:",
                "Yangi loyiha (import)",
                "Loyiha-yangi");
            if (string.IsNullOrWhiteSpace(name)) return null;
            return (true, 0, name.Trim());
        }

        // Mavjud loyiha — joriy tanlangan bo'lsa undan foydalanamiz
        if (state.CurrentProject != null)
        {
            var confirm = MessageBox.Show(
                $"Ma'lumotlar «{state.CurrentProject.Name}» loyihasiga yoziladi.\n\n" +
                "Excel dagi quduqlar (mavjud bo'lmasa) shu loyihaga qo'shiladi.\n" +
                "Davom etasizmi?",
                "Tasdiqlash",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return null;
            return (false, state.CurrentProject.Id, state.CurrentProject.Name);
        }

        // Joriy loyiha yo'q — nom so'raymiz
        var projectNames = string.Join("\n  • ", projects.Select(p => p.Name));
        var chosen = Views.PromptDialog.Ask(
            "Loyiha nomini kiriting (mavjud yoki yangi):\n\nMavjud:\n  • " + projectNames,
            "Loyiha tanlash",
            projects.First().Name);
        if (string.IsNullOrWhiteSpace(chosen)) return null;

        var existing = projects.FirstOrDefault(p =>
            p.Name.Equals(chosen.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return (false, existing.Id, existing.Name);

        return (true, 0, chosen.Trim());
    }

    static void Warn(string msg) => MessageBox.Show(msg, "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
}