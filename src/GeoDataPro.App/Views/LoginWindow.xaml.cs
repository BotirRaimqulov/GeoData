using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GeoDataPro.Core;
using GeoDataPro.Core.Security;

namespace GeoDataPro.App.Views;

public partial class LoginWindow : Window
{
    readonly SecurityHost _host;
    readonly bool _bootstrap;
    readonly CancellationTokenSource _cts = new();
    bool _busy;

    public LoginWindow(SecurityHost host, bool bootstrap)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _bootstrap = bootstrap;
        InitializeComponent();

        ModeText.Text = bootstrap ? "Birinchi ishga tushirish" : "Tizimga kirish";
        SubmitButton.Content = bootstrap ? "Yaratish" : "Kirish";
        ConfirmPanel.Visibility = bootstrap ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Text = bootstrap
            ? "Administrator hisobini yarating."
            : string.Empty;

        Loaded += (_, _) => UserBox.Focus();
        Closed += (_, _) => _cts.Cancel();
    }

    public bool Authenticated { get; private set; }

    async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SubmitButton.IsEnabled = false;

        try
        {
            if (_bootstrap) await ProvisionAsync().ConfigureAwait(true);
            else await SignInAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            var report = _host.Errors.Describe(ex, "Amalni bajarib bo'lmadi.");
            MessageText.Text = report.UserMessage + " (" + report.Reference + ")";
        }
        finally
        {
            _busy = false;
            SubmitButton.IsEnabled = true;
        }
    }

    async Task SignInAsync()
    {
        var result = await _host.Authentication
            .LoginAsync(UserBox.Text, PassBox.Password, _cts.Token)
            .ConfigureAwait(true);

        PassBox.Clear();

        if (result.Succeeded)
        {
            Authenticated = true;
            DialogResult = true;
            Close();
            return;
        }

        MessageText.Text = Describe(result);
        PassBox.Focus();
    }

    async Task ProvisionAsync()
    {
        if (!string.Equals(PassBox.Password, ConfirmBox.Password, StringComparison.Ordinal))
        {
            MessageText.Text = "Parollar mos kelmadi.";
            return;
        }

        var created = await _host.Authentication
            .ProvisionFirstAdminAsync(UserBox.Text, UserBox.Text, PassBox.Password, _cts.Token)
            .ConfigureAwait(true);

        if (!created.Succeeded)
        {
            MessageText.Text = Describe(created);
            return;
        }

        var result = await _host.Authentication
            .LoginAsync(UserBox.Text, PassBox.Password, _cts.Token)
            .ConfigureAwait(true);

        PassBox.Clear();
        ConfirmBox.Clear();

        if (result.Succeeded)
        {
            Authenticated = true;
            DialogResult = true;
            Close();
            return;
        }

        MessageText.Text = Describe(result);
    }

    static string Describe(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.InvalidCredentials => "Foydalanuvchi nomi yoki parol noto'g'ri.",
        AuthOutcome.Disabled => "Hisob faol emas.",
        AuthOutcome.Conflict => "Bunday foydalanuvchi allaqachon mavjud.",
        AuthOutcome.LockedOut => "Hisob vaqtincha bloklandi. " + Wait(result.RetryAfter),
        AuthOutcome.Throttled => "Juda ko'p urinish. " + Wait(result.RetryAfter),
        AuthOutcome.PasswordRejected => DescribeRejection(result.Rejection),
        AuthOutcome.NotAuthenticated => "Seans tugagan.",
        _ => "Amalni bajarib bo'lmadi.",
    };

    static string Wait(TimeSpan retryAfter) =>
        retryAfter > TimeSpan.Zero
            ? Math.Ceiling(retryAfter.TotalSeconds).ToString("0") + " soniyadan keyin urinib ko'ring."
            : "Keyinroq urinib ko'ring.";

    static string DescribeRejection(PasswordRejection reason) => reason switch
    {
        PasswordRejection.TooShort => "Parol kamida 12 belgidan iborat bo'lishi kerak.",
        PasswordRejection.TooLong => "Parol juda uzun.",
        PasswordRejection.NotComplex => "Parolda katta/kichik harf, raqam va belgi bo'lishi kerak.",
        PasswordRejection.ContainsIdentity => "Parolda foydalanuvchi nomi bo'lmasin.",
        PasswordRejection.Common => "Bu parol juda oson topiladi.",
        PasswordRejection.Repetitive => "Parol juda takrorlanuvchan.",
        _ => "Parol qabul qilinmadi.",
    };
}
