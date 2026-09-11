using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GeoDataPro.App.Services;
using GeoDataPro.Core.Services;
using GeoDataPro.App.ViewModels;

namespace GeoDataPro.App.Views;

public partial class JournalView : UserControl
{
    public JournalView()
    {
        InitializeComponent();
        Loaded += JournalView_Loaded;
        DescBox.SelectionChanged += DescBox_SelectionChanged;
    }

    /// <summary>
    /// DataGrid ko'p tanlovni (Ctrl+Click / Shift+Click) ViewModel.SelectedItems bilan sinxronlaydi.
    /// WPF DataGrid.SelectedItems faqat o'qish uchun — koddan binding qilib bo'lmaydi,
    /// shu sababli SelectionChanged event orqali qo'lda sinxronlaymiz.
    /// </summary>
    void JournalView_Loaded(object sender, RoutedEventArgs e)
    {
        if (Grid == null) return;
        Grid.SelectionChanged -= Grid_SelectionChanged;
        Grid.SelectionChanged += Grid_SelectionChanged;
    }

    void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        var journal = vm?.Journal;
        if (journal == null) return;

        // Tashqaridan o'zgartirish (dasturiy) ekanligini bildiruvchi flag.
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            foreach (var removed in e.RemovedItems.OfType<JournalRowVm>())
                journal.SelectedItems.Remove(removed);
            foreach (var added in e.AddedItems.OfType<JournalRowVm>())
                if (!journal.SelectedItems.Contains(added))
                    journal.SelectedItems.Add(added);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    bool _syncingSelection;

    /// <summary>
    /// Litol. kod / Rang / Tekstura ustunlari ComboBox bilan tahrirlanadi. Standart DataGrid
    /// xatti-harakatida katakcha birinchi bosishda faqat tahrirlash rejimiga o'tadi, ammo
    /// ComboBox ro'yxati ochilmaydi — foydalanuvchi buni ikkinchi marta bosishga majbur bo'ladi.
    /// Shablon to'liq yuklanishini kutib (Dispatcher), ro'yxatni darhol ochamiz.
    /// </summary>
    void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is ComboBox combo)
            OpenCombo(combo);
    }

    void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        var isEditing = IsCellEditing(grid);

        if (e.Key == Key.Delete && !isEditing)
        {
            var vm = DataContext as MainViewModel;
            var cmd = vm?.Journal?.DeleteRowCommand;
            if (cmd?.CanExecute(null) == true)
            {
                cmd.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back && !isEditing)
        {
            var col = grid.CurrentCell.Column;
            if (col == null || col.IsReadOnly) return;

            grid.BeginEdit();
            grid.Dispatcher.BeginInvoke(() =>
            {
                var item = grid.CurrentItem;
                if (item == null) return;
                var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row == null) return;
                var cell = grid.CurrentCell.Column?.GetCellContent(row)?.Parent as DataGridCell;
                if (cell?.IsEditing == true && FindInTree<TextBox>(cell) is { } box)
                {
                    box.Clear();
                    box.SelectAll();
                    box.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }
    }

    static bool IsCellEditing(DataGrid grid)
    {
        var item = grid.CurrentItem;
        if (item == null) return false;
        var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (row == null) return false;
        return grid.Columns.Any(c => (c.GetCellContent(row)?.Parent as DataGridCell)?.IsEditing == true);
    }

    static T? FindInTree<T>(DependencyObject? node) where T : DependencyObject
    {
        if (node == null) return null;
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) return match;
            if (FindInTree<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        // Don't intercept scroll when a ComboBox dropdown is open in the current cell
        if (IsAnyComboDropdownOpen(grid)) return;

        var scroller = FindScrollViewer(grid);
        if (scroller == null) return;

        bool horizontal = Keyboard.Modifiers == ModifierKeys.Shift
                          || scroller.ScrollableHeight <= 0;

        if (!horizontal) return;
        if (scroller.ScrollableWidth <= 0) return;

        var step = e.Delta / 120d * 64d;
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - step);
        e.Handled = true;
    }

    static bool IsAnyComboDropdownOpen(DataGrid grid)
    {
        var item = grid.CurrentItem;
        if (item == null) return false;
        var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (row == null) return false;
        return FindInTree<ComboBox>(row) is { IsDropDownOpen: true };
    }

    static ScrollViewer? FindScrollViewer(DependencyObject? node)
    {
        if (node == null) return null;

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } deeper) return deeper;
        }

        return null;
    }

    void EditCombo_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox combo)
            OpenCombo(combo);
    }

    void EditCombo_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox combo && !combo.IsDropDownOpen)
        {
            combo.Focus();
            OpenCombo(combo);
            e.Handled = true;
        }
    }

    static void OpenCombo(ComboBox combo)
    {
        combo.Dispatcher.BeginInvoke(new Action(() =>
        {
            combo.IsDropDownOpen = true;
            combo.MaxDropDownHeight = 320;
        }), DispatcherPriority.Input);
    }

    // ==================== Kern tavsifi: so'z tanlansa muqobil variantlar ====================

    Popup? _variantPopup;

    /// <summary>
    /// Foydalanuvchi "Kern tavsifi" matnida bir so'zni (masalan ikki marta bosib) tanlaganida,
    /// shu so'z qaysi spravochnikka (Litho/Rang/Tekstura/Mineral/Donadorlik) tegishli ekanini
    /// aniqlaymiz va o'sha spravochnikning boshqa qiymatlarini taklif sifatida ko'rsatamiz.
    /// Variant tanlansa — matndagi so'z almashadi VA mos ustun ham bir vaqtda sozlanadi.
    /// </summary>
    void DescBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        CloseVariantPopup();

        var box = DescBox;
        var word = box.SelectedText?.Trim() ?? "";
        if (word.Length < 2) return;
        if (box.DataContext is not JournalRowVm row) return;

        if (!TryResolveVariants(word, row, out var category, out var options, out var apply) || options.Count == 0)
            return;

        int start = box.SelectionStart;
        int length = box.SelectionLength;

        ShowVariantPopup(box, category, options, picked =>
        {
            var full = box.Text ?? "";
            if (start < 0 || start + length > full.Length) return;
            apply(picked.Value);
            box.Text = full.Remove(start, length).Insert(start, picked.Name);
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.CaretIndex = start + picked.Name.Length;
            box.Focus();
        });
    }

    /// <summary>
    /// Tanlangan so'zni barcha spravochniklar (+ donadorlik) nomlari bilan solishtiradi.
    /// Faqat AYNAN BITTA spravochnikka mos kelsa (ikkilanish bo'lmasa) — natija qaytaradi;
    /// aks holda hech narsa taklif qilinmaydi (noto'g'ri taxmindan ko'ra jim turish xavfsizroq).
    /// </summary>
    static bool TryResolveVariants(string word, JournalRowVm row, out string category,
        out List<(object? Value, string Name)> options, out Action<object?> apply)
    {
        var rc = RefCache.Instance;
        bool Matches(string? name) => !string.IsNullOrWhiteSpace(name) && string.Equals(name.Trim(), word, StringComparison.OrdinalIgnoreCase);

        var litho = rc.Litho.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));
        var color = rc.Colors.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));
        var texture = rc.Textures.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));
        var mineral = rc.Minerals.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));
        var floraFauna = rc.FloraFauna.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));
        var ironHydroxide = rc.IronHydroxides.FirstOrDefault(x => Matches(x.Name) || Matches(x.NameRu));

        var grain = JournalRowVm.GrainSizes.FirstOrDefault(g => string.Equals(g, word, StringComparison.OrdinalIgnoreCase));
        var hardness = JournalRowVm.Hardnesses.FirstOrDefault(g => string.Equals(g, word, StringComparison.OrdinalIgnoreCase));
        var cementation = JournalRowVm.Cementations.FirstOrDefault(g => string.Equals(g, word, StringComparison.OrdinalIgnoreCase));

        int hits = (litho != null ? 1 : 0) + (color != null ? 1 : 0) + (texture != null ? 1 : 0)
                 + (mineral != null ? 1 : 0) + (floraFauna != null ? 1 : 0)
                 + (ironHydroxide != null ? 1 : 0)
                 + (grain != null ? 1 : 0) + (hardness != null ? 1 : 0) + (cementation != null ? 1 : 0);

        if (hits == 1)
        {
            if (litho != null)
            {
                category = "Litologik kod";
                options = rc.Litho.Where(x => x.Code != litho.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.LithoCode = (int?)v;
                return true;
            }
            if (color != null)
            {
                category = "Rang";
                options = rc.Colors.Where(x => x.Code != color.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.ColorCode = (int?)v;
                return true;
            }
            if (texture != null)
            {
                category = "Tarkibi";
                options = rc.Textures.Where(x => x.Code != texture.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.TextureCode = (int?)v;
                return true;
            }
            if (mineral != null)
            {
                category = "Mineralizatsiya";
                options = rc.Minerals.Where(x => x.Code != mineral.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.MineralCode = (int?)v;
                return true;
            }
            if (floraFauna != null)
            {
                category = "Flora-Fauna";
                options = rc.FloraFauna.Where(x => x.Code != floraFauna.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.FloraFaunaCode = (int?)v;
                return true;
            }
            if (ironHydroxide != null)
            {
                category = "Gidrookisleniya";
                options = rc.IronHydroxides.Where(x => x.Code != ironHydroxide.Code).Select(x => ((object?)x.Code, x.Name)).ToList();
                apply = v => row.IronHydroxideCode = (int?)v;
                return true;
            }
            if (hardness != null)
            {
                category = "Qattiqligi";
                options = JournalRowVm.Hardnesses.Where(g => !string.Equals(g, hardness, StringComparison.OrdinalIgnoreCase))
                                                  .Select(g => ((object?)g, Capitalize(g))).ToList();
                apply = v => row.Hardness = (string?)v;
                return true;
            }
            if (cementation != null)
            {
                category = "Sementlashuvi";
                options = JournalRowVm.Cementations.Where(g => !string.Equals(g, cementation, StringComparison.OrdinalIgnoreCase))
                                                    .Select(g => ((object?)g, Capitalize(g))).ToList();
                apply = v => row.Cementation = (string?)v;
                return true;
            }
            // grain != null
            category = "Donadorligi";
            options = JournalRowVm.GrainSizes.Where(g => !string.Equals(g, grain, StringComparison.OrdinalIgnoreCase))
                                              .Select(g => ((object?)g, Capitalize(g))).ToList();
            apply = v => row.GrainSize = (string?)v;
            return true;
        }

        category = "";
        options = new List<(object?, string)>();
        apply = _ => { };
        return false;
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    void ShowVariantPopup(TextBox anchor, string category, List<(object? Value, string Name)> options,
        Action<(object? Value, string Name)> onPick)
    {
        // Diqqat: bu klassda x:Name="Grid" bilan DataGrid mavjud (JournalView.xaml), shu sabab
        // "Grid" qisqa nomi shu maydonni soyalab qo'yadi — panel turi to'liq nom bilan yoziladi.
        var header = new System.Windows.Controls.Grid { Margin = new Thickness(10, 8, 6, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = category, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x85)),
        };
        System.Windows.Controls.Grid.SetColumn(title, 0);

        var close = new TextBlock
        {
            Text = "✕", FontSize = 11, Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3)),
            Margin = new Thickness(10, 0, 0, 0),
        };
        close.MouseLeftButtonUp += (_, _) => CloseVariantPopup();
        System.Windows.Controls.Grid.SetColumn(close, 1);

        header.Children.Add(title);
        header.Children.Add(close);

        var list = new ListBox { BorderThickness = new Thickness(0), MaxHeight = 220, Margin = new Thickness(0, 0, 0, 4) };
        list.ItemsSource = options.Select(o => o.Name).ToList();
        list.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                var picked = options[list.SelectedIndex];
                CloseVariantPopup();
                onPick(picked);
            }
        };

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(list);

        var shell = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)),
            BorderThickness = new Thickness(1),
            MinWidth = 190,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 3, Opacity = 0.14, Color = Color.FromRgb(0x1F, 0x29, 0x37),
            },
            Child = panel,
        };

        Rect caretRect;
        try { caretRect = anchor.GetRectFromCharacterIndex(anchor.SelectionStart); }
        catch { caretRect = new Rect(0, anchor.ActualHeight, 0, 0); }

        _variantPopup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = Math.Max(caretRect.Left, 0),
            VerticalOffset = caretRect.Bottom + 4,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = shell,
        };
        _variantPopup.Closed += (_, _) => _variantPopup = null;
        _variantPopup.IsOpen = true;
    }

    void CloseVariantPopup()
    {
        if (_variantPopup == null) return;
        var p = _variantPopup;
        _variantPopup = null;
        p.IsOpen = false;
    }

    // ==================== Mineral tarkibi: multi-select ComboBox ====================

    bool _clasticToggling;

    void ClasticComboItem_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBoxItem cbi && cbi.DataContext is CheckableClasticItem item)
        {
            _clasticToggling = true;
            item.IsChecked = !item.IsChecked;
            e.Handled = true;
            // Flag is reset only inside DropDownClosed to avoid race with that event.
        }
    }

    void ClasticCombo_DropDownClosed(object sender, EventArgs e)
    {
        if (_clasticToggling && sender is ComboBox combo)
        {
            _clasticToggling = false;
            combo.Dispatcher.BeginInvoke(() => combo.IsDropDownOpen = true, DispatcherPriority.Input);
        }
        else
        {
            _clasticToggling = false;
        }
    }

    void ClasticCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem != null)
            combo.SelectedItem = null;
    }
}