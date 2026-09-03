using System;
using System.IO;
using System.Windows;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Services;

public static class AppNotifier
{
    const string Title = "GeoData Pro";

    static string LogPath =>
        Path.Combine(Path.GetDirectoryName(AppDbContext.DbPath) ?? AppContext.BaseDirectory, "errors.log");

    public static void Info(string message) => Show(message, MessageBoxImage.Information);

    public static void Warn(string message) => Show(message, MessageBoxImage.Warning);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex != null)
            LogException(ex, message);

        Show(ex == null ? message : $"{message}\n\n{ex.Message}", MessageBoxImage.Error);
    }

    public static void LogException(Exception ex, string context)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging should never crash the app.
        }
    }

    static void Show(string message, MessageBoxImage image)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => MessageBox.Show(message, Title, MessageBoxButton.OK, image));
            return;
        }

        MessageBox.Show(message, Title, MessageBoxButton.OK, image);
    }
}
