using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GeoDataPro.App.Views;

public partial class SecretField : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SecretField),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(SecretField),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaskedProperty =
        DependencyProperty.Register(nameof(Masked), typeof(bool), typeof(SecretField),
            new PropertyMetadata(true, OnMaskedChanged));

    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(SecretField),
            new PropertyMetadata(false, OnStateChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    public bool Masked
    {
        get => (bool)GetValue(MaskedProperty);
        set => SetValue(MaskedProperty, value);
    }

    bool _syncing;

    public SecretField()
    {
        InitializeComponent();

        Secret.GotKeyboardFocus += OnFocusChanged;
        Secret.LostKeyboardFocus += OnFocusChanged;
        Plain.GotKeyboardFocus += OnFocusChanged;
        Plain.LostKeyboardFocus += OnFocusChanged;

        Loaded += (_, _) =>
        {
            ApplyMask();
            ApplyState();
        };
    }

    public event EventHandler? ValueChanged;

    public string Value => ShowingPlain ? Plain.Text : Secret.Password;

    bool ShowingPlain => !Masked || Reveal.IsChecked == true;

    public new bool Focus()
    {
        var target = ShowingPlain ? (Control)Plain : Secret;
        return target.Focus();
    }

    public void Clear()
    {
        _syncing = true;
        try
        {
            Secret.Clear();
            Plain.Clear();
        }
        finally
        {
            _syncing = false;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    void Secret_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    void Plain_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    void Reveal_Changed(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            if (Reveal.IsChecked == true)
            {
                Plain.Text = Secret.Password;
                Plain.Visibility = Visibility.Visible;
                Secret.Visibility = Visibility.Collapsed;
                Plain.CaretIndex = Plain.Text.Length;
                Plain.Focus();
            }
            else
            {
                Secret.Password = Plain.Text;
                Secret.Visibility = Visibility.Visible;
                Plain.Visibility = Visibility.Collapsed;
                Secret.Focus();
            }
        }
        finally
        {
            _syncing = false;
        }

        ApplyState();
    }

    static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SecretField)d).ApplyState();

    static void OnMaskedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (SecretField)d;
        field.ApplyMask();
        field.ApplyState();
    }

    void ApplyMask()
    {
        if (Shell is null) return;

        if (Masked)
        {
            Reveal.Visibility = Visibility.Visible;
            var revealed = Reveal.IsChecked == true;
            Plain.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
            Secret.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        Reveal.Visibility = Visibility.Collapsed;
        Reveal.IsChecked = false;
        Plain.Visibility = Visibility.Visible;
        Secret.Visibility = Visibility.Collapsed;
    }

    void OnFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => ApplyState();

    void ApplyState()
    {
        if (Shell is null) return;

        var focused = Secret.IsKeyboardFocusWithin || Plain.IsKeyboardFocusWithin;

        var key = HasError ? "Danger" : focused ? "Accent" : "BorderStrong";
        if (TryFindResource(key) is Brush border) Shell.BorderBrush = border;

        var iconKey = HasError ? "Danger" : focused ? "Accent" : "TextMuted";
        if (TryFindResource(iconKey) is Brush icon) LeadIcon.Fill = icon;
    }
}
