using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GeoDataPro.App.Services;
using GeoDataPro.App.Views;
using GeoDataPro.Core;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Security;
using GeoDataPro.Platform.Windows.Security;

namespace GeoDataPro.App;

public partial class App : Application
{
    SecurityHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            var paths = new WindowsPlatformPaths();
            var storage = new WindowsSecureStorage(paths.KeyDirectory);

#if DEBUG
            const DiagnosticLevel level = DiagnosticLevel.Debug;
#else
            const DiagnosticLevel level = DiagnosticLevel.Warning;
#endif

            _host = SecurityHost.Build(paths, storage, AppInfo.Version, level);
            AppNotifier.Attach(_host);

            var readiness = _host.PrepareStorage();

            if (!readiness.DatabaseEncrypted)
                AppNotifier.Warn(
                    "Diqqat: ma'lumotlar bazasi shifrlanmagan holatda ochildi." + Environment.NewLine +
                    "Maxfiy ma'lumotlar himoyalanmagan. Administratorga murojaat qiling.");
            else if (readiness.PlaintextRescueFile != null)
                AppNotifier.Info(
                    "Baza shifrlandi. Eski shifrlanmagan nusxa saqlab qo'yildi — " +
                    "uni xavfsiz joyga ko'chiring yoki butunlay o'chiring.");
        }
        catch (Exception ex)
        {
            AppNotifier.Startup("Ilovani ishga tushirib bo'lmadi.", ex);
            Shutdown(-1);
            return;
        }

        if (!Authenticate())
        {
            Shutdown(0);
        }
    }

    bool Authenticate()
    {
        var host = _host;
        if (host == null) return false;

        try
        {
            var bootstrap = !host.Authentication.HasAnyUserAsync().GetAwaiter().GetResult();
            var login = new LoginWindow(host, bootstrap);
            bool success = false;

            // LoginWindow fires this event after auth succeeds but before closing itself,
            // so the spinner is still visible while MainWindow is being constructed.
            login.ReadyToLaunchMainWindow += () =>
            {
                success = true;
                string step = "attach";
                try
                {
                    AppState.Instance.Attach(host);
                    step = "window-create";
                    var window = new MainWindow();
                    step = "window-show";
                    MainWindow = window;
                    window.Closed += (_, _) => Shutdown(0);
                    window.Show();
                }
                catch (Exception ex)
                {
                    AppNotifier.Startup($"Ilovani ishga tushirib bo'lmadi. [{step}]", ex);
                    success = false;
                }
                finally
                {
                    login.CloseAfterReady();
                }
            };

            // PushFrame keeps the dispatcher running (processes UI events) while the
            // login window is open; frame exits when the window closes.
            var frame = new System.Windows.Threading.DispatcherFrame();
            login.Closed += (_, _) => frame.Continue = false;
            login.Show();
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            return success;
        }
        catch (Exception ex)
        {
            AppNotifier.Startup("Kirish oynasini ochib bo'lmadi.", ex);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Authentication.LogoutAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        _host?.Dispose();
        _host = null;
        base.OnExit(e);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppNotifier.Error("Oxirgi amal bajarilmadi.", e.Exception);
        e.Handled = true;
    }

    void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new InvalidOperationException("E_UNKNOWN");
        AppNotifier.Error("Ilovada tuzatib bo'lmaydigan xato yuz berdi.", ex);
    }

    void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppNotifier.LogException(e.Exception, "background");
        e.SetObserved();
    }
}

public static class AppInfo
{
    public const string Version = "1.1.0";
}
