using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Services;

/// <summary>Global tanlov holati: joriy loyiha va quduq.</summary>
public partial class AppState : ObservableObject
{
    public static AppState Instance { get; } = new();

    [ObservableProperty] private List<Project> _projects = new();
    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private Well? _currentWell;

    public event Action? WellChanged;
    public event Action? DataChanged;

    public List<Well> CurrentWells =>
        CurrentProject?.Wells.OrderBy(w => w.Number).ToList() ?? new();

    partial void OnCurrentProjectChanged(Project? value)
    {
        OnPropertyChanged(nameof(CurrentWells));
        CurrentWell = CurrentWells.FirstOrDefault();
    }

    partial void OnCurrentWellChanged(Well? value) =>
        RaiseSafely(WellChanged, "Quduq ma'lumotlarini yuklashda xato yuz berdi. Ayrim bo'limlar yangilanmagan bo'lishi mumkin.");

    public void RaiseDataChanged() =>
        RaiseSafely(DataChanged, "Ma'lumotlarni yangilashda xato yuz berdi.");

    public void Reload(int? keepProjectId = null, int? keepWellId = null)
    {
        using var db = new AppDbContext();
        Projects = db.Projects
            .OrderBy(p => p.Name)
            .Select(p => p)
            .ToList();
        // load wells for each project
        foreach (var p in Projects)
            p.Wells = db.Wells.Where(w => w.ProjectId == p.Id).OrderBy(w => w.Number).ToList();

        var pid = keepProjectId ?? CurrentProject?.Id;
        CurrentProject = Projects.FirstOrDefault(p => p.Id == pid) ?? Projects.FirstOrDefault();

        if (keepWellId is int wid)
            CurrentWell = CurrentProject?.Wells.FirstOrDefault(w => w.Id == wid) ?? CurrentWells.FirstOrDefault();
    }

    static void RaiseSafely(Action? handlers, string message)
    {
        if (handlers == null) return;

        Exception? firstError = null;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                firstError ??= ex;
                AppNotifier.LogException(ex,
                    $"{handler.Method.DeclaringType?.FullName ?? "Unknown"}.{handler.Method.Name}");
            }
        }

        if (firstError != null)
            AppNotifier.Error(message, firstError);
    }
}
