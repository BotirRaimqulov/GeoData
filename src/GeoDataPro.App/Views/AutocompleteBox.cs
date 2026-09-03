using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace GeoDataPro.App.Views;

/// <summary>
/// TytBox + inline (ghost) autocomplete. Tab yoki → tugmasi taklifni qabul qiladi,
/// pastda mos variantlar ro'yxati chiqadi (↑/↓ + Enter bilan tanlash mumkin).
/// </summary>
public class AutocompleteBox : TextBox
{
    Popup? _popup;
    ListBox? _list;
    TextBlock? _ghost;
    bool _internalChange;

    public static readonly DependencyProperty SuggestionsProperty =
        DependencyProperty.Register(nameof(Suggestions), typeof(IEnumerable), typeof(AutocompleteBox),
            new PropertyMetadata(null));

    /// <summary>Taklif manbai (string yoki .ToString() bo'ladigan obyektlar).</summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(AutocompleteBox),
            new PropertyMetadata(""));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public AutocompleteBox()
    {
        AcceptsReturn = false;
        VerticalContentAlignment = VerticalAlignment.Top;
        TextWrapping = TextWrapping.Wrap;
        Loaded += (_, _) => BuildAdornments();
        TextChanged += OnTextChanged;
        LostFocus += (_, _) => ClosePopup();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    void BuildAdornments()
    {
        if (_popup != null) return;

        _ghost = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3)),
            IsHitTestVisible = false,
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSize,
            FontFamily = FontFamily,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.6,
        };

        // ghost'ni TextBox ustiga qo'yish uchun AdornerLayer ishlatamiz
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer != null) layer.Add(new GhostAdorner(this, _ghost));

        _list = new ListBox
        {
            MaxHeight = 220,
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
        };
        _list.PreviewMouseLeftButtonUp += (_, _) => AcceptFromList();

        var shell = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 4, 0, 4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.14,
                Color = Color.FromRgb(0x1F, 0x29, 0x37),
            },
            Child = _list,
        };

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = false,
            VerticalOffset = 4,
            Child = shell,
        };
    }

    IEnumerable<string> AllSuggestions() =>
        (Suggestions?.Cast<object?>() ?? Enumerable.Empty<object?>())
            .Select(x => x switch
            {
                null => null,
                string s => s,
                _ => TryGetText(x),
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    static string? TryGetText(object x)
    {
        var p = x.GetType().GetProperty("Text");
        return p?.GetValue(x) as string ?? x.ToString();
    }

    void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_internalChange) return;
        // Faqat foydalanuvchi yozayotganda (fokusda) taklif ochamiz.
        if (!IsKeyboardFocusWithin)
        {
            SetGhost("", isPlaceholder: false);
            ClosePopup();
            return;
        }
        RefreshSuggestions();
    }

    void RefreshSuggestions()
    {
        var typed = Text ?? "";
        if (_ghost != null)
            _ghost.MaxWidth = Math.Max(ActualWidth - Padding.Left - Padding.Right, 0);

        if (typed.Length == 0)
        {
            SetGhost(Placeholder, isPlaceholder: true);
            ClosePopup();
            return;
        }

        var matches = AllSuggestions()
            .Where(s => s.StartsWith(typed, StringComparison.OrdinalIgnoreCase) &&
                        !s.Equals(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Length)
            .Take(8)
            .ToList();

        // Ichida qidiruv (StartsWith topilmasa)
        if (matches.Count == 0)
        {
            matches = AllSuggestions()
                .Where(s => s.Contains(typed, StringComparison.OrdinalIgnoreCase) &&
                            !s.Equals(typed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Length)
                .Take(8)
                .ToList();
        }

        var inline = matches.FirstOrDefault(s => s.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
        if (inline != null)
            SetGhost(inline.Substring(typed.Length), isPlaceholder: false);
        else
            SetGhost("", isPlaceholder: false);

        if (matches.Count > 0)
        {
            if (_list != null) { _list.ItemsSource = matches; _list.SelectedIndex = -1; }
            if (_popup != null) { _popup.MinWidth = ActualWidth; _popup.IsOpen = true; }
        }
        else ClosePopup();
    }

    void SetGhost(string text, bool isPlaceholder)
    {
        if (_ghost == null) return;
        _ghost.Text = text;
        _ghost.Padding = isPlaceholder ? Padding : default;
        _ghost.Margin = isPlaceholder ? default : SuggestionMargin();
        _ghost.Opacity = isPlaceholder ? 0.65 : 0.55;
        _ghost.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    Thickness SuggestionMargin()
    {
        if (string.IsNullOrEmpty(Text))
            return new Thickness(Padding.Left, Padding.Top, 0, 0);

        try
        {
            var caretRect = GetRectFromCharacterIndex(Text.Length, true);
            return new Thickness(Padding.Left + Math.Max(caretRect.Left, 0), Padding.Top + Math.Max(caretRect.Top, 0), 0, 0);
        }
        catch
        {
            return new Thickness(Padding.Left, Padding.Top, 0, 0);
        }
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_popup?.IsOpen == true && _list != null)
        {
            if (e.Key == Key.Down)
            {
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && _list.SelectedItem is string sel)
            {
                Commit(sel);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ClosePopup();
                e.Handled = true;
                return;
            }
        }

        // Tab / → — inline taklifni qabul qilish
        if ((e.Key == Key.Tab || e.Key == Key.Right) && _ghost is { Visibility: Visibility.Visible } g
            && !string.IsNullOrEmpty(g.Text) && g.Text.Length > (Text?.Length ?? 0)
            && CaretIndex == (Text?.Length ?? 0))
        {
            Commit(g.Text);
            e.Handled = true;
        }
    }

    void AcceptFromList()
    {
        if (_list?.SelectedItem is string s) Commit(s);
    }

    void Commit(string value)
    {
        _internalChange = true;
        Text = value;
        CaretIndex = value.Length;
        _internalChange = false;
        SetGhost("", isPlaceholder: false);
        ClosePopup();
        // binding yangilansin
        GetBindingExpression(TextProperty)?.UpdateSource();
    }

    void ClosePopup()
    {
        if (_popup != null) _popup.IsOpen = false;
    }

    /// <summary>TextBox ustida ko'rinadigan xira (ghost) matn.</summary>
    sealed class GhostAdorner : Adorner
    {
        readonly TextBlock _child;
        readonly VisualCollection _visuals;

        public GhostAdorner(UIElement target, TextBlock child) : base(target)
        {
            _child = child;
            _visuals = new VisualCollection(this) { _child };
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _child.Width = ((FrameworkElement)AdornedElement).ActualWidth;
            _child.Measure(constraint);
            return ((FrameworkElement)AdornedElement).RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
