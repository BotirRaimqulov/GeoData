using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using GeoDataPro.App.Data;
using GeoDataPro.App.ViewModels;

namespace GeoDataPro.App.Views;

public partial class ReferenceView : UserControl
{
    public ReferenceView()
    {
        InitializeComponent();
        Loaded += ReferenceView_Loaded;
    }

    void ReferenceView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        foreach (var grid in new[] { LithoGrid, ColorsGrid, TexturesGrid, MineralsGrid, FloraFaunaGrid, DescGrid })
        {
            if (grid == null) continue;
            grid.SelectionChanged -= Grid_SelectionChanged;
            grid.SelectionChanged += Grid_SelectionChanged;
        }
    }

    bool _syncingSelection;
    void Grid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var vm = DataContext as ReferenceViewModel;
        if (vm == null) return;
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            foreach (var removed in e.RemovedItems.OfType<object>())
                vm.SelectedItems.Remove(removed);
            foreach (var added in e.AddedItems.OfType<object>())
                if (!vm.SelectedItems.Contains(added))
                    vm.SelectedItems.Add(added);
        }
        finally { _syncingSelection = false; }
    }

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