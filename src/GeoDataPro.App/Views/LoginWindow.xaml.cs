using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using GeoDataPro.Core;
using GeoDataPro.Core.Security;

namespace GeoDataPro.App.Views;

public partial class LoginWindow : Window
{
    readonly SecurityHost _host;
    readonly bool _bootstrap;
    readonly CancellationTokenSource _cts = new();
    bool _busy;

    /// <summary>
    /// Fired by the login window after authentication succeeds, while the loading overlay
    /// is still visible. The subscriber (App.cs) should create and show the main window,
    /// then call <see cref="CloseAfterReady"/> to dismiss this window.
    /// </summary>
    public event Action? ReadyToLaunchMainWindow;

    public LoginWindow(SecurityHost host, bool bootstrap)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _bootstrap = bootstrap;
        InitializeComponent();

        if (bootstrap)
        {
            HeadingText.Text = "Birinchi ishga tushirish";
            SubheadingText.Text = "Tizimni birinchi marta sozlash uchun ma'lumot kiriting";
            SubmitButton.Content = "Yaratish";
            ConfirmField.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Visible;
            MessageText.Text = "Administrator hisobini yarating.";
        }
        else
        {
            HeadingText.Text = "Xush kelibsiz";
            SubheadingText.Text = "Davom etish uchun hisobingizga kiring";
            SubmitButton.Content = "Kirish";
            ConfirmField.Visibility = Visibility.Collapsed;
            HintText.Visibility = Visibility.Collapsed;
            MessageText.Text = string.Empty;
        }

        ConfirmField.ValueChanged += (_, _) => RefreshMatch();
        PassField.ValueChanged += (_, _) => RefreshMatch();

        Loaded += (_, _) => UserField.Focus();
        Closed += (_, _) => _cts.Cancel();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public bool Authenticated { get; private set; }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_busy) return;
        Close();
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
        if (!_bootstrap) return;

        var confirm = ConfirmField.Value;
        ConfirmField.HasError = confirm.Length > 0 &&
                                !string.Equals(PassField.Value, confirm, StringComparison.Ordinal);
    }

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
            Fail(report.UserMessage + " (" + report.Reference + ")");
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
            .LoginAsync(UserField.Value, PassField.Value, _cts.Token)
            .ConfigureAwait(true);

        PassField.Clear();

        if (result.Succeeded)
        {
            Succeed();
            return;
        }

        PassField.HasError = true;
        UserField.HasError = result.Outcome == AuthOutcome.InvalidCredentials;
        Fail(Describe(result));
        PassField.Focus();
    }

    async Task ProvisionAsync()
    {
        UserField.HasError = false;
        PassField.HasError = false;

        if (string.IsNullOrWhiteSpace(UserField.Value))
        {
            UserField.HasError = true;
            Fail("Foydalanuvchi nomini kiriting.");
            UserField.Focus();
            return;
        }

        if (!string.Equals(PassField.Value, ConfirmField.Value, StringComparison.Ordinal))
        {
            ConfirmField.HasError = true;
            Fail("Parollar mos kelmadi.");
            ConfirmField.Focus();
            return;
        }

        var name = UserField.Value.Trim();

        var created = await _host.Authentication
            .ProvisionFirstAdminAsync(name, name, PassField.Value, _cts.Token)
            .ConfigureAwait(true);

        if (!created.Succeeded)
        {
            PassField.HasError = created.Outcome == AuthOutcome.PasswordRejected;
            UserField.HasError = created.Outcome is AuthOutcome.Conflict or AuthOutcome.InvalidCredentials;
            Fail(Describe(created));
            return;
        }

        // Switch to login view briefly so user sees the two-step flow
        HeadingText.Text = "Xush kelibsiz";
        SubheadingText.Text = "Hisob yaratildi — tizimga kirish amalga oshirilmoqda...";
        ConfirmField.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        SubmitButton.Visibility = Visibility.Collapsed;

        var savedPass = PassField.Value;
        PassField.Clear();
        ConfirmField.Clear();

        ShowLoadingOverlay("Kirish amalga oshirilmoqda...");

        var result = await _host.Authentication
            .LoginAsync(name, savedPass, _cts.Token)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            OverlayText.Text = "Ilova yuklanmoqda...";
            Authenticated = true;
            ReadyToLaunchMainWindow?.Invoke();
            return;
        }

        // Auto-login failed: restore login UI
        LoadingOverlay.Visibility = Visibility.Collapsed;
        SubmitButton.Visibility = Visibility.Visible;
        SubmitButton.Content = "Kirish";
        ConfirmField.Visibility = Visibility.Collapsed;
        UserField.SetText(name);
        Fail(Describe(result));
    }

    void Succeed()
    {
        Authenticated = true;
        ShowLoadingOverlay("Ilova yuklanmoqda...");
        ReadyToLaunchMainWindow?.Invoke();
        // Window stays open showing overlay; App.cs calls CloseAfterReady() when done.
    }

    public void CloseAfterReady()
    {
        // Invoked by App.cs after MainWindow is shown.
        Dispatcher.Invoke(() => { Authenticated = true; Close(); });
    }

    void ShowLoadingOverlay(string message)
    {
        OverlayText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
        StartSpinner();
    }

    void StartSpinner()
    {
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = null,
        };
        SpinnerAngle.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim);
    }

    void Fail(string message)
    {
        MessageText.Text = message;
        if (TryFindResource("Danger") is System.Windows.Media.Brush danger)
            MessageText.Foreground = danger;
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
        PasswordRejection.NotComplex => "Parolda katta/kichik harf, raqam va maxsus belgi bo'lishi kerak.",
        PasswordRejection.ContainsIdentity => "Parolda foydalanuvchi nomi bo'lmasin.",
        PasswordRejection.Common => "Bu parol juda oson topiladi.",
        PasswordRejection.Repetitive => "Parol juda takrorlanuvchan.",
        _ => "Parol qabul qilinmadi.",
    };
}
