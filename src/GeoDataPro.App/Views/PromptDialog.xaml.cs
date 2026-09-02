using System.Windows;

namespace GeoDataPro.App.Views;

public partial class PromptDialog : Window
{
    public string Value => Input.Text.Trim();

    public PromptDialog(string prompt, string title, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    public static string? Ask(string prompt, string title, string initial = "")
    {
        var dlg = new PromptDialog(prompt, title, initial) { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true && dlg.Value.Length > 0 ? dlg.Value : null;
    }
}
