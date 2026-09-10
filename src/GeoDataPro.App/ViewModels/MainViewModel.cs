using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.Core.Data;
using GeoDataPro.App.Services;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;
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
    public ReferenceViewModel FloraFaunaRef { get; } = new(ReferenceViewModel.Kind.FloraFauna);
    public ReferenceViewModel IronHydroxideRef { get; } = new(ReferenceViewModel.Kind.IronHydroxide);
    public ReferenceViewModel ClasticMaterialRef { get; } = new(ReferenceViewModel.Kind.ClasticMaterial);
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
        "florafauna" => "Flora-Fauna",
        "ironhydroxide" => "Gidrookisleniya",
        "clasticmaterial" => "Mineral tarkibi",
        "descriptions" => "Tavsif shablonlari",
        "wells" => "Loyiha va quduq boshqaruvi",
        "io" => "Import / Eksport",
        _ => "GeoData Pro",
    };
    [ObservableProperty] private string _statusText = "Barcha o'zgarishlar saqlangan";
    [ObservableProperty] private string _clock = DateTime.Now.ToString("HH:mm:ss");

    public string DbLabel => "Baza: Himoyalangan lokal";
    public string Version => "v" + AppInfo.Version;

    public string UserLabel
    {
        get
        {
            var principal = State.Principal;
            return principal == null ? string.Empty : principal.DisplayName + " · " + principal.Role;
        }
    }

    public bool CanImport => State.Can(Permissions.Import);
    public bool CanExport => State.Can(Permissions.Export);
    public bool CanBackup => State.Can(Permissions.Backup);
    public bool CanEditReferences => State.Can(Permissions.ReferenceWrite);
    public bool CanManageUsers => State.Can(Permissions.UserManage);
    public bool CanReadAudit => State.Can(Permissions.AuditRead);

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
        SubscribeUnsaved(FloraFaunaRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(IronHydroxideRef, nameof(ReferenceViewModel.HasUnsaved));
        SubscribeUnsaved(ClasticMaterialRef, nameof(ReferenceViewModel.HasUnsaved));
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
                          || MineralRef.HasUnsaved || FloraFaunaRef.HasUnsaved
                          || IronHydroxideRef.HasUnsaved || ClasticMaterialRef.HasUnsaved
                          || DescriptionRef.HasUnsaved;
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

        Well well;
        try
        {
            well = State.Data.CreateWell(project.Id, name);
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
    async Task ImportExcelAsync()
    {
        if (!CanImport) { Warn("Bu amal uchun ruxsatingiz yo'q."); return; }

        var dlg = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            Title = "Excel import",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true,
        };
        if (dlg.ShowDialog() != true) return;

        var projectChoice = AskProjectForImport();
        if (projectChoice == null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            var result = projectChoice.Value.createNew
                ? await State.Host.Transfer.ImportAsync(dlg.FileName, null, projectChoice.Value.name, cts.Token)
                : await State.Host.Transfer.ImportAsync(dlg.FileName, projectChoice.Value.projectId, null, cts.Token);

            State.Reload();
            var targetProject = State.Projects.FirstOrDefault(p =>
                string.Equals(p.Name, result.ProjectName, StringComparison.OrdinalIgnoreCase));
            if (targetProject != null)
                State.Reload(targetProject.Id);

            RefCache.Instance.Reload();
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
                $"  \u2192 {wellsList}\n\n" +
                $"  Dala jurnali: {result.JournalRows} qator\n" +
                $"  Namuna: {result.SampleRows} qator\n" +
                $"  SRP: {result.SrpRows} nuqta",
                "GeoData Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Importni yakunlab bo'lmadi.", ex);
        }
    }

    /// <summary>
    /// Excel eksport: joriy loyihadagi BARCHA quduqlar (yoki faqat joriy quduq — tanlov).
    /// </summary>
    [RelayCommand]
    async Task ExportExcelAsync()
    {
        if (!CanExport) { Warn("Bu amal uchun ruxsatingiz yo'q."); return; }

        var project = State.CurrentProject;
        if (project == null) { Warn("Avval loyiha tanlang."); return; }

        var choice = MessageBox.Show(
            $"Loyiha: {project.Name}\n\n" +
            "Barcha quduqlarni eksport qilishni xohlaysizmi?\n\n" +
            "  Ha  \u2014 loyihadagi barcha quduqlar\n" +
            "  Yo'q \u2014 faqat joriy tanlangan quduq",
            "Eksport rejimi",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel) return;

        bool exportAll = choice == MessageBoxResult.Yes;

        if (!exportAll && State.CurrentWell == null)
        {
            Warn("Joriy quduq tanlanmagan.");
            return;
        }

        var defaultName = exportAll
            ? SafeName(project.Name + "_barcha_quduqlar")
            : SafeName(project.Name + "_" + State.CurrentWell!.Number);

        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = defaultName + ".xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != true) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            if (exportAll)
                await State.Host.Transfer.ExportProjectAsync(dlg.FileName, project.Id, null, cts.Token);
            else
                await State.Host.Transfer.ExportWellAsync(dlg.FileName, State.CurrentWell!.Id, cts.Token);

            AppNotifier.Info("Eksport tayyor.");
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Eksportni yakunlab bo'lmadi.", ex);
        }
    }

    [RelayCommand]
    async Task BackupAsync()
    {
        if (!CanBackup) { Warn("Bu amal uchun ruxsatingiz yo'q."); return; }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try
        {
            await State.Host.Backup.CreateAsync(cts.Token);
            AppNotifier.Info("Zaxira nusxa yaratildi.");
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Zaxira nusxani yaratib bo'lmadi.", ex);
        }
    }

    [RelayCommand]
    void ChangePassword()
    {
        var dialog = new Views.ChangePasswordWindow(State.Host)
        {
            Owner = Application.Current?.MainWindow,
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    void SignOut()
    {
        if (MessageBox.Show("Tizimdan chiqasizmi?", "Tasdiqlash",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            State.Host.Authentication.LogoutAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppNotifier.LogException(ex, "signout");
        }

        Application.Current?.Shutdown();
    }

    static string SafeName(string value) =>
        GeoDataPro.Core.Files.SafeFile.SanitizeFileName(value, "eksport");

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