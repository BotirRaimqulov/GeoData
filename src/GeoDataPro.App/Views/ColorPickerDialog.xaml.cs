using System;
using System.Windows;
using System.Windows.Media;

namespace GeoDataPro.App.Views;

public partial class ColorPickerDialog : Window
{
    bool _updating;

    public string SelectedHex { get; private set; } = "#CBD5E1";

    public ColorPickerDialog(string initialHex)
    {
        InitializeComponent();
        ApplyColor(ParseColor(initialHex));
    }

    public static bool TryPick(string initialHex, out string selectedHex)
    {
        var dialog = new ColorPickerDialog(initialHex)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            selectedHex = dialog.SelectedHex;
            return true;
        }

        selectedHex = initialHex;
        return false;
    }

    void ApplyColor(Color color)
    {
        _updating = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        HexBox.Text = SelectedHex;
        PreviewSwatch.Background = new SolidColorBrush(color);
        _updating = false;
    }

    static Color ParseColor(string? hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#CBD5E1" : hex)!;
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString("#CBD5E1")!;
        }
    }

    void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        ApplyColor(Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value));
    }

    void HexBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        var color = ParseColor(HexBox.Text);
        ApplyColor(color);
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
