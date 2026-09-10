using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
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
        Loaded += (_, _) => CurrentField.Focus();
        Closed += (_, _) => _cts.Cancel();
        ConfirmField.ValueChanged += (_, _) => RefreshMatch();
        NewField.ValueChanged += (_, _) => RefreshMatch();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !_busy) Close(); };
    }

    void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void RefreshMatch()
    {
        var confirm = ConfirmField.Value;
        ConfirmField.HasError = confirm.Length > 0 &&
                                !string.Equals(NewField.Value, confirm, StringComparison.Ordinal);
    }

    async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (!string.Equals(NewField.Value, ConfirmField.Value, StringComparison.Ordinal))
        {
            ConfirmField.HasError = true;
            MessageText.Text = "Parollar mos kelmadi.";
            ConfirmField.Focus();
            return;
        }

        _busy = true;
        SubmitButton.IsEnabled = false;

        try
        {
            var result = await _host.Authentication
                .ChangePasswordAsync(CurrentField.Value, NewField.Value, _cts.Token)
                .ConfigureAwait(true);

            CurrentField.Clear();
            NewField.Clear();
            ConfirmField.Clear();

            if (result.Succeeded)
            {
                DialogResult = true;
                Close();
                return;
            }

            CurrentField.HasError = result.Outcome == AuthOutcome.InvalidCredentials;
            NewField.HasError = result.Outcome == AuthOutcome.PasswordRejected;
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
