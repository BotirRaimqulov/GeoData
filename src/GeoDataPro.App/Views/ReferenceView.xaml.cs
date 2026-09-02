using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeoDataPro.App.Views;

public partial class ReferenceView : UserControl
{
    public ReferenceView() => InitializeComponent();

    /// <summary>Litho/Rang/Tekstura/Mineral bog'lash ustunlari ComboBox bilan tahrirlanadi —
    /// ro'yxatni birinchi bosishdayoq ochib beramiz (JournalView'dagi bilan bir xil sabab).</summary>
    void DescGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is ComboBox combo)
            combo.Dispatcher.BeginInvoke(new Action(() => combo.IsDropDownOpen = true), DispatcherPriority.Input);
    }
}
