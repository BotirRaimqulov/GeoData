using System;
using System.Threading;
using System.Windows;
using GeoDataPro.Core;
using GeoDataPro.Core.Security;

namespace GeoDataPro.App.Views;

public partial class ChangePasswordWindow : Window
{
    readonly SecurityHost _host;
    readonly CancellationTokenSource _cts = new();
    bool _busy;

    public ChangePasswordWindow(SecurityHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        InitializeComponent();
        Loaded += (_, _) => CurrentBox.Focus();
        Closed += (_, _) => _cts.Cancel();
    }

    async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (!string.Equals(NewBox.Password, ConfirmBox.Password, StringComparison.Ordinal))
        {
            MessageText.Text = "Parollar mos kelmadi.";
            return;
        }

        _busy = true;
        SubmitButton.IsEnabled = false;

        try
        {
            var result = await _host.Authentication
                .ChangePasswordAsync(CurrentBox.Password, NewBox.Password, _cts.Token)
                .ConfigureAwait(true);

            CurrentBox.Clear();
            NewBox.Clear();
            ConfirmBox.Clear();

            if (result.Succeeded)
            {
                DialogResult = true;
                Close();
                return;
            }

            MessageText.Text = Describe(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var report = _host.Errors.Describe(ex, "Parolni o'zgartirib bo'lmadi.");
            MessageText.Text = report.UserMessage + " (" + report.Reference + ")";
        }
        finally
        {
            _busy = false;
            SubmitButton.IsEnabled = true;
        }
    }

    static string Describe(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.InvalidCredentials => "Joriy parol noto'g'ri.",
        AuthOutcome.NotAuthenticated => "Seans tugagan. Qaytadan kiring.",
        AuthOutcome.Disabled => "Hisob faol emas.",
        AuthOutcome.PasswordRejected => result.Rejection switch
        {
            PasswordRejection.TooShort => "Parol kamida 12 belgidan iborat bo'lishi kerak.",
            PasswordRejection.TooLong => "Parol juda uzun.",
            PasswordRejection.NotComplex => "Parolda katta/kichik harf, raqam va belgi bo'lishi kerak.",
            PasswordRejection.ContainsIdentity => "Parolda foydalanuvchi nomi bo'lmasin.",
            PasswordRejection.Common => "Bu parol juda oson topiladi.",
            PasswordRejection.Repetitive => "Yangi parol eskisidan farq qilishi kerak.",
            _ => "Parol qabul qilinmadi.",
        },
        _ => "Parolni o'zgartirib bo'lmadi.",
    };
}
