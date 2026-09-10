using System;
using System.Windows;
using GeoDataPro.Core;
using GeoDataPro.Core.Diagnostics;
using GeoDataPro.Core.Files;
using GeoDataPro.Core.Security;

namespace GeoDataPro.App.Services;

public static class AppNotifier
{
    const string Title = "GeoData Pro";
    static SecurityHost? _host;

    public static void Attach(SecurityHost host) => _host = host;

    public static void Info(string message) => Show(message, MessageBoxImage.Information);

    public static void Warn(string message) => Show(message, MessageBoxImage.Warning);

    public static void Error(string message, Exception? ex = null)
    {
        if (ex == null)
        {
            Show(message, MessageBoxImage.Error);
            return;
        }

        var friendly = Translate(ex, message);
        var host = _host;

        if (host == null)
        {
            Show(friendly, MessageBoxImage.Error);
            return;
        }

        var report = host.Errors.Describe(ex, friendly);
        Show(report.UserMessage + Environment.NewLine + Environment.NewLine + "Kod: " + report.Reference,
            MessageBoxImage.Error);
    }

    public static void LogException(Exception ex, string context) =>
        _host?.Log.Write(DiagnosticLevel.Error, context, ex);

    static string Translate(Exception ex, string fallback) => ex switch
    {
        NotAuthenticatedException => "Seans tugagan. Qaytadan kiring.",
        SessionExpiredException => "Seans muddati tugadi. Qaytadan kiring.",
        SecurityDeniedException => "Bu amal uchun ruxsatingiz yo'q.",
        IntegrityViolationException => "Yozuv butunligi buzilgan. Amal bekor qilindi.",
        FileGuardException guard => TranslateFile(guard.Reason),
        OperationCanceledException => "Amal bekor qilindi.",
        _ => fallback,
    };

    static string TranslateFile(FileRejection reason) => reason switch
    {
        FileRejection.Missing => "Fayl topilmadi.",
        FileRejection.Empty => "Fayl bo'sh.",
        FileRejection.TooLarge => "Fayl hajmi juda katta.",
        FileRejection.BadExtension => "Fayl turi qo'llab-quvvatlanmaydi.",
        FileRejection.BadSignature => "Fayl formati noto'g'ri.",
        FileRejection.Traversal or FileRejection.OutsideRoot => "Fayl manzili qabul qilinmadi.",
        FileRejection.ArchiveTooLarge or FileRejection.ArchiveTooManyEntries => "Fayl ichidagi ma'lumot juda katta.",
        FileRejection.ArchiveUnsafeEntry => "Fayl tarkibi xavfsiz emas.",
        _ => "Faylni o'qib bo'lmadi.",
    };

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
