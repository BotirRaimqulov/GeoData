using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GeoDataPro.App.Infrastructure;

/// <summary>"qumtosh.png" (kategoriya param) -> pack URI ImageSource.</summary>
public class PatternToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var file = value as string;
        var folder = parameter as string ?? "litho";
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{folder}/{file}", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>HEX string -> SolidColorBrush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        try
        {
            var hex = value as string;
            if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch { return Brushes.Transparent; }
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>null / bo'sh -> Collapsed.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        bool has = value is string s ? !string.IsNullOrWhiteSpace(s) : value != null;
        if (Invert) has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>CurrentSection == parameter ? true : (unset).</summary>
public class SectionEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c)
        => value is true ? parameter! : Binding.DoNothing;
}

/// <summary>CurrentSection == parameter ? Visible : Collapsed.</summary>
public class SectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>enum/qiymat.ToString() == parameter -> Visible, aks holda Collapsed.</summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
        => string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
