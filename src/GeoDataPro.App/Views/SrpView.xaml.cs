using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoDataPro.Core.Data;
using GeoDataPro.App.ViewModels;

namespace GeoDataPro.App.Views;

public partial class SrpView : UserControl
{
    public SrpView()
    {
        InitializeComponent();
        Loaded += SrpView_Loaded;
    }

    void SrpView_Loaded(object sender, RoutedEventArgs e)
    {
        if (Grid == null) return;
        Grid.SelectionChanged -= Grid_SelectionChanged;
        Grid.SelectionChanged += Grid_SelectionChanged;
    }

    bool _syncingSelection;
    void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as SrpViewModel;
        if (vm == null) return;
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            foreach (var removed in e.RemovedItems.OfType<SrpRow>())
                vm.SelectedItems.Remove(removed);
            foreach (var added in e.AddedItems.OfType<SrpRow>())
                if (!vm.SelectedItems.Contains(added))
                    vm.SelectedItems.Add(added);
        }
        finally { _syncingSelection = false; }
    }

    void ScaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Chart == null) return;
        if (ScaleBox?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (!double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var window)) return;

        Chart.WindowMetres = window;
    }

    void ResetZoom_Click(object sender, RoutedEventArgs e) => Chart?.ResetView();
}