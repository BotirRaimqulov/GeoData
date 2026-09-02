using System;
using System.Globalization;
using System.Windows.Data;

namespace GeoDataPro.App.Views;

/// <summary>norm (0..1) + container size -> canvas offset (padding hisobga olingan).</summary>
public class CanvasScaleConverter : IMultiValueConverter
{
    public string Axis { get; set; } = "X";
    public double Padding { get; set; } = 12;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double norm || values[1] is not double size || size <= 0)
            return 0d;
        double usable = Math.Max(size - 2 * Padding, 1);
        return Padding + norm * usable - 3; // -3 centers the 6px dot
    }

    public object[] ConvertBack(object value, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
}
