using System;
using System.Windows.Controls;
using System.Windows.Threading;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Views;

public partial class ReferenceView : UserControl
{
    public ReferenceView() => InitializeComponent();
    void DescGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is ComboBox combo)
            combo.Dispatcher.BeginInvoke(new Action(() => combo.IsDropDownOpen = true), DispatcherPriority.Input);
    }

    void PickColor_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: object item }) return;

        switch (item)
        {
            case ColorCode color when ColorPickerDialog.TryPick(color.HexColor, out var pickedColor):
                color.HexColor = pickedColor;
                break;
            case LithoCode litho when ColorPickerDialog.TryPick(litho.HexColor ?? "#CBD5E1", out var pickedLitho):
                litho.HexColor = pickedLitho;
                break;
        }
    }
}
