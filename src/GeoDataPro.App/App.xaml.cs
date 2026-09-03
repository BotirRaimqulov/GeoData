using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            using var db = new AppDbContext();
            db.EnsureSeeded();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Ilovani ishga tushirishda kutilmagan xato yuz berdi.", ex);
            Shutdown(-1);
        }
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppNotifier.Error("Ilovada kutilmagan xato yuz berdi. Oxirgi amal bajarilmadi.", e.Exception);
        e.Handled = true;
    }

    void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception("Noma'lum xato");
        AppNotifier.Error("Ilovada tuzatib bo'lmaydigan xato yuz berdi.", ex);
    }

    void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppNotifier.Error("Fon vazifasida kutilmagan xato yuz berdi.", e.Exception);
        e.SetObserved();
    }
}
