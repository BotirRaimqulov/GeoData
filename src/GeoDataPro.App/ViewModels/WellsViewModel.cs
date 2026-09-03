using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

public partial class WellsViewModel : ObservableObject
{
    readonly AppState _state = AppState.Instance;

    public ObservableCollection<Project> Projects { get; } = new();
    public ObservableCollection<WellListItem> Wells { get; } = new();

    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private WellListItem? _selectedWell;

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

    public void Load()
    {
        Projects.Clear();
        using var db = new AppDbContext();
        foreach (var p in db.Projects.OrderBy(p => p.Name)) Projects.Add(p);
        SelectedProject = Projects.FirstOrDefault(p => p.Id == _state.CurrentProject?.Id) ?? Projects.FirstOrDefault();
        RaiseStatsChanged();
    }

    void LoadWells()
    {
        Wells.Clear();
        if (SelectedProject == null) return;
        using var db = new AppDbContext();
        var wells = db.Wells.Where(w => w.ProjectId == SelectedProject.Id).OrderBy(w => w.Number).ToList();
        foreach (var w in wells)
        {
            int jr = db.JournalRows.Count(r => r.WellId == w.Id);
            int sr = db.SampleRows.Count(r => r.WellId == w.Id);
            Wells.Add(new WellListItem(w) { JournalCount = jr, SampleCount = sr });
        }
        RaiseStatsChanged();
    }

    [RelayCommand]
    void AddProject()
    {
        var name = Views.PromptDialog.Ask("Yangi loyiha nomi:", "Loyiha qo'shish", "Loyiha-yangi");
        if (string.IsNullOrWhiteSpace(name)) return;
        using var db = new AppDbContext();
        db.Projects.Add(new Project { Name = name.Trim() });
        db.SaveChanges();
        Load();
        _state.Reload();
    }

    [RelayCommand]
    void AddWell()
    {
        if (SelectedProject == null) return;
        var num = Views.PromptDialog.Ask("Yangi quduq raqami:", "Quduq qo'shish", "0000");
        if (string.IsNullOrWhiteSpace(num)) return;
        using var db = new AppDbContext();
        db.Wells.Add(new Well { ProjectId = SelectedProject.Id, Number = num.Trim() });
        db.SaveChanges();
        LoadWells();
        _state.Reload(SelectedProject.Id);
    }

    [RelayCommand]
    void SaveWell()
    {
        if (SelectedWell == null) return;
        using var db = new AppDbContext();
        var w = db.Wells.Find(SelectedWell.Model.Id);
        if (w == null) return;
        w.Number = SelectedWell.Number;
        w.RigNumber = SelectedWell.RigNumber;
        w.StartDepth = SelectedWell.StartDepth;
        w.EndDepth = SelectedWell.EndDepth;
        w.StartDate = SelectedWell.StartDate;
        w.EndDate = SelectedWell.EndDate;
        w.Geologist = SelectedWell.Geologist;
        db.SaveChanges();
        LoadWells();
        _state.Reload(SelectedProject?.Id, w.Id);
        MessageBox.Show("Quduq saqlandi.", "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    void DeleteWell()
    {
        if (SelectedWell == null) return;
        if (MessageBox.Show($"'{SelectedWell.Number}' quduqni va unga bog'liq barcha ma'lumotlarni o'chirasizmi?",
            "Tasdiqlash", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using var db = new AppDbContext();
        var id = SelectedWell.Model.Id;
        db.JournalRows.RemoveRange(db.JournalRows.Where(r => r.WellId == id));
        db.SampleRows.RemoveRange(db.SampleRows.Where(r => r.WellId == id));
        db.SrpRows.RemoveRange(db.SrpRows.Where(r => r.WellId == id));
        var w = db.Wells.Find(id);
        if (w != null) db.Wells.Remove(w);
        db.SaveChanges();
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
