using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GeoDataPro.App.Views;

public partial class JournalView : UserControl
{
    public JournalView() => InitializeComponent();

    /// <summary>
    /// Litol. kod / Rang / Tekstura ustunlari ComboBox bilan tahrirlanadi. Standart DataGrid
    /// xatti-harakatida katakcha birinchi bosishda faqat tahrirlash rejimiga o'tadi, ammo
    /// ComboBox ro'yxati ochilmaydi — foydalanuvchi buni ikkinchi marta bosishga majbur bo'ladi.
    /// Shablon to'liq yuklanishini kutib (Dispatcher), ro'yxatni darhol ochamiz.
    /// </summary>
    void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is ComboBox combo)
            combo.Dispatcher.BeginInvoke(new Action(() => combo.IsDropDownOpen = true), DispatcherPriority.Input);
    }
}
