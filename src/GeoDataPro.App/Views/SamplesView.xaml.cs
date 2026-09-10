using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoDataPro.Core.Data;
using GeoDataPro.App.ViewModels;

namespace GeoDataPro.App.Views;

public partial class SamplesView : UserControl
{
    public SamplesView()
    {
        InitializeComponent();
        Loaded += SamplesView_Loaded;
    }

    void SamplesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (Grid == null) return;
        Grid.SelectionChanged -= Grid_SelectionChanged;
        Grid.SelectionChanged += Grid_SelectionChanged;
    }

    bool _syncingSelection;
    void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as SamplesViewModel;
        if (vm == null) return;
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            foreach (var removed in e.RemovedItems.OfType<SampleRow>())
                vm.SelectedItems.Remove(removed);
            foreach (var added in e.AddedItems.OfType<SampleRow>())
                if (!vm.SelectedItems.Contains(added))
                    vm.SelectedItems.Add(added);
        }
        finally { _syncingSelection = false; }
    }
}