using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.Core.Data;
using GeoDataPro.App.Services;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;

namespace GeoDataPro.App.ViewModels;

public partial class WellsViewModel : ObservableObject
{
    readonly AppState _state = AppState.Instance;

    public ObservableCollection<Project> Projects { get; } = new();
    public ObservableCollection<WellListItem> Wells { get; } = new();

    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private WellListItem? _selectedWell;
    [ObservableProperty] private bool _hasUnsaved;

    public int ProjectCount => Projects.Count;
    public int WellCount => Wells.Count;
    public int TotalJournalCount => Wells.Sum(x => x.JournalCount);
    public int TotalSampleCount => Wells.Sum(x => x.SampleCount);

    public WellsViewModel()
    {
        _state.DataChanged += Load;
        Load();
    }

    partial void OnSelectedProjectChanged(Project? value) => LoadWells();

    partial void OnSelectedWellChanged(WellListItem? value)
    {
        // Yangi quduq tanlanganda — eski tanlovning property change larini bekor qilamiz.
        if (_subscribedWell != null)
        {
            _subscribedWell.PropertyChanged -= Well_PropertyChanged;
            _subscribedWell = null;
        }

        // Yangi tanlovni kuzatamiz.
        if (value != null)
        {
            _subscribedWell = value;
            value.PropertyChanged += Well_PropertyChanged;
        }

        HasUnsaved = false;
    }

    WellListItem? _subscribedWell;

    void Well_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => HasUnsaved = true;

    public bool CanManageProjects => _state.Can(Permissions.ProjectUpdate);
    public bool CanCreateProjects => _state.Can(Permissions.ProjectCreate);
    public bool CanDeleteWells => _state.Can(Permissions.ProjectDelete);

    public void Load()
    {
        Projects.Clear();
        foreach (var p in _state.Data.GetProjects()) Projects.Add(p);
        SelectedProject = Projects.FirstOrDefault(p => p.Id == _state.CurrentProject?.Id) ?? Projects.FirstOrDefault();
        RaiseStatsChanged();
    }

    void LoadWells()
    {
        Wells.Clear();
        if (SelectedProject == null) return;
        foreach (var w in _state.Data.GetWells(SelectedProject.Id))
        {
            Wells.Add(new WellListItem(w)
            {
                JournalCount = _state.Data.CountJournalRows(w.Id),
                SampleCount = _state.Data.CountSampleRows(w.Id),
            });
        }
        RaiseStatsChanged();
    }

    [RelayCommand]
    void AddProject()
    {
        var name = Views.PromptDialog.Ask("Yangi loyiha nomi:", "Loyiha qo'shish", "Loyiha-yangi");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            _state.Data.CreateProject(name);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Loyihani saqlab bo'lmadi.", ex);
            return;
        }

        Load();
        _state.Reload();
    }

    [RelayCommand]
    void AddWell()
    {
        if (SelectedProject == null) return;
        var num = Views.PromptDialog.Ask("Yangi quduq raqami:", "Quduq qo'shish", "0000");
        if (string.IsNullOrWhiteSpace(num)) return;
        try
        {
            _state.Data.CreateWell(SelectedProject.Id, num);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Quduqni saqlab bo'lmadi.", ex);
            return;
        }

        LoadWells();
        _state.Reload(SelectedProject.Id);
    }

    [RelayCommand]
    void SaveWell()
    {
        if (SelectedWell == null) return;

        if (string.IsNullOrWhiteSpace(SelectedWell.Number))
        {
            AppNotifier.Warn("Quduq raqamini kiriting.");
            return;
        }

        if (SelectedWell.StartDepth.HasValue && SelectedWell.EndDepth.HasValue &&
            SelectedWell.EndDepth.Value < SelectedWell.StartDepth.Value)
        {
            AppNotifier.Warn("Tugatish chuqurligi boshlanish chuqurligidan kichik bo'lmasligi kerak.");
            return;
        }

        var edited = new Well
        {
            Id = SelectedWell.Model.Id,
            ProjectId = SelectedWell.Model.ProjectId,
            Number = SelectedWell.Number.Trim(),
            RigNumber = SelectedWell.RigNumber,
            StartDepth = SelectedWell.StartDepth,
            EndDepth = SelectedWell.EndDepth,
            StartDate = SelectedWell.StartDate,
            EndDate = SelectedWell.EndDate,
            Geologist = SelectedWell.Geologist,
        };

        try
        {
            _state.Data.UpdateWell(edited);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Quduqni saqlab bo'lmadi.", ex);
            return;
        }

        LoadWells();
        _state.Reload(SelectedProject?.Id, edited.Id);
        HasUnsaved = false;
        AppNotifier.Info("Quduq saqlandi.");
    }

    [RelayCommand]
    void DeleteWell()
    {
        if (SelectedWell == null) return;
        if (MessageBox.Show($"'{SelectedWell.Number}' quduq arxivga o'tkaziladi. Davom etasizmi?",
            "Tasdiqlash", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            _state.Data.SoftDeleteWell(SelectedWell.Model.Id);
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Quduqni o'chirib bo'lmadi.", ex);
            return;
        }

        LoadWells();
        _state.Reload(SelectedProject?.Id);
    }

    void RaiseStatsChanged()
    {
        OnPropertyChanged(nameof(ProjectCount));
        OnPropertyChanged(nameof(WellCount));
        OnPropertyChanged(nameof(TotalJournalCount));
        OnPropertyChanged(nameof(TotalSampleCount));
    }
}

public partial class WellListItem : ObservableObject
{
    public Well Model { get; }
    public WellListItem(Well w)
    {
        Model = w;
        _number = w.Number; _rigNumber = w.RigNumber;
        _startDepth = w.StartDepth; _endDepth = w.EndDepth;
        _startDate = w.StartDate; _endDate = w.EndDate; _geologist = w.Geologist;
    }
    [ObservableProperty] private string _number = "";
    [ObservableProperty] private string? _rigNumber;
    [ObservableProperty] private double? _startDepth;
    [ObservableProperty] private double? _endDepth;
    [ObservableProperty] private string? _startDate;
    [ObservableProperty] private string? _endDate;
    [ObservableProperty] private string? _geologist;
    public int JournalCount { get; set; }
    public int SampleCount { get; set; }
}