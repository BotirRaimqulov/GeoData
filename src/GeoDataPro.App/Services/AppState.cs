using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoDataPro.Core;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;

namespace GeoDataPro.App.Services;

public partial class AppState : ObservableObject
{
    public static AppState Instance { get; } = new();

    SecurityHost? _host;

    [ObservableProperty] private List<Project> _projects = new();
    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private Well? _currentWell;

    public event Action? WellChanged;
    public event Action? DataChanged;

    public SecurityHost Host => _host ?? SecurityHost.Require();

    public IGeoDataService Data => Host.Data;

    public Principal? Principal => Host.Sessions.Current;

    public bool Can(string permission) => _host?.Authorization.Has(permission) ?? false;

    public void Attach(SecurityHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        RefCache.Instance.Attach(host.Data);
    }

    public List<Well> CurrentWells =>
        CurrentProject?.Wells.OrderBy(w => w.Number).ToList() ?? new();

    partial void OnCurrentProjectChanged(Project? value)
    {
        OnPropertyChanged(nameof(CurrentWells));
        CurrentWell = CurrentWells.FirstOrDefault();
    }

    partial void OnCurrentWellChanged(Well? value) =>
        RaiseSafely(WellChanged, "Quduq ma'lumotlarini yuklashda xato yuz berdi.");

    public void RaiseDataChanged() =>
        RaiseSafely(DataChanged, "Ma'lumotlarni yangilashda xato yuz berdi.");

    public void Reload(int? keepProjectId = null, int? keepWellId = null)
    {
        if (_host == null) return;

        try
        {
            var projects = Data.GetProjects().ToList();
            foreach (var project in projects)
                project.Wells = Data.GetWells(project.Id).ToList();
            Projects = projects;
        }
        catch (SecurityDeniedException)
        {
            Projects = new List<Project>();
        }

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
                AppNotifier.LogException(ex, "state-handler");
            }
        }

        if (firstError != null)
            AppNotifier.Error(message, firstError);
    }
}
