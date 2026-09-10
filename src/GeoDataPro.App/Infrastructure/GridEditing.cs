using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GeoDataPro.App.Infrastructure;

public static class GridEditing
{
    public static readonly DependencyProperty EnhancedProperty =
        DependencyProperty.RegisterAttached(
            "Enhanced",
            typeof(bool),
            typeof(GridEditing),
            new PropertyMetadata(false, OnEnhancedChanged));

    public static void SetEnhanced(DependencyObject element, bool value) =>
        element.SetValue(EnhancedProperty, value);

    public static bool GetEnhanced(DependencyObject element) =>
        (bool)element.GetValue(EnhancedProperty);

    static void OnEnhancedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        grid.PreparingCellForEdit -= OnPreparingCellForEdit;
        grid.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;

        if (!Equals(e.NewValue, true)) return;

        grid.PreparingCellForEdit += OnPreparingCellForEdit;
        grid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    static void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        switch (e.EditingElement)
        {
            case TextBox box:
                Focus(box, () =>
                {
                    box.SelectAll();
                    box.Focus();
                });
                break;

            case ComboBox { IsEditable: true } combo:
                Focus(combo, () =>
                {
                    combo.Focus();
                    if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox inner)
                        inner.SelectAll();
                });
                break;

            default:
                if (FindDescendant<TextBox>(e.EditingElement) is { } nested)
                    Focus(nested, () =>
                    {
                        nested.SelectAll();
                        nested.Focus();
                    });
                break;
        }
    }

    static void OnPreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.ClickCount != 1) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is not DependencyObject source) return;

        var cell = FindAncestor<DataGridCell>(source);
        if (cell is null || cell.IsEditing || cell.IsReadOnly) return;
        if (!cell.IsSelected) return;
        if (FindAncestor<System.Windows.Controls.Primitives.DataGridColumnHeader>(source) is not null) return;

        grid.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (cell.IsEditing || cell.IsReadOnly) return;
            if (!ReferenceEquals(FindAncestor<DataGrid>(cell), grid)) return;
            cell.Focus();
            grid.BeginEdit();
        }), DispatcherPriority.Input);
    }

    static void Focus(UIElement element, Action action) =>
        element.Dispatcher.BeginInvoke(action, DispatcherPriority.Input);

    static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    static T? FindDescendant<T>(DependencyObject? node) where T : DependencyObject
    {
        if (node is null) return null;

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }

        return null;
    }
}

public sealed class EditableNumberConverter : IValueConverter
{
    public string Format { get; set; } = "0.###";

    public double Fallback { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? Format;

        return value switch
        {
            double d when double.IsFinite(d) => d.ToString(format, CultureInfo.InvariantCulture),
            float f when float.IsFinite(f) => ((double)f).ToString(format, CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            null => string.Empty,
            _ => string.Empty,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim();
        var nullable = Nullable.GetUnderlyingType(targetType) != null;

        if (string.IsNullOrEmpty(text)) return nullable ? null : Box(Fallback, targetType);
        if (text is "-" or "." or "," or "-." or "-,") return nullable ? null : Box(Fallback, targetType);

        var normalized = text.Replace(',', '.');

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return DependencyProperty.UnsetValue;

        if (!double.IsFinite(parsed)) return DependencyProperty.UnsetValue;

        return Box(parsed, targetType);
    }

    static object Box(double value, Type targetType)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (target == typeof(int)) return (int)Math.Round(value);
        if (target == typeof(float)) return (float)value;
        if (target == typeof(decimal)) return (decimal)value;
        return value;
    }
}
