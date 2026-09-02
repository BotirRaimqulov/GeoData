using System.Windows;
using GeoDataPro.App.Data;

namespace GeoDataPro.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var db = new AppDbContext();
        db.EnsureSeeded();
    }
}
