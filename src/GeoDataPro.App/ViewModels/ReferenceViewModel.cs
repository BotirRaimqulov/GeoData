using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

/// <summary>Litologik kodlar / ranglar / teksturalar / minerallar spravochnigi tahrirlagichi.</summary>
public partial class ReferenceViewModel : ObservableObject
{
    public enum Kind { Litho, Color, Texture, Mineral, Description }
    public Kind CurrentKind { get; }

    public ObservableCollection<LithoCode> Litho { get; } = new();
    public ObservableCollection<ColorCode> Colors { get; } = new();
    public ObservableCollection<TextureCode> Textures { get; } = new();
    public ObservableCollection<MineralCode> Minerals { get; } = new();
    public ObservableCollection<DescriptionTemplate> Descriptions { get; } = new();

    [ObservableProperty] private object? _selected;
    public string Title { get; }

    public ReferenceViewModel(Kind kind)
    {
        CurrentKind = kind;
        Title = kind switch
        {
            Kind.Litho => "Litologik kodlar",
            Kind.Color => "Kern ranglari",
            Kind.Texture => "Teksturalar",
            Kind.Mineral => "Mineralizatsiya",
            _ => "Tavsif shablonlari",
        };
        Load();
    }

    public void Load()
    {
        using var db = new AppDbContext();
        Litho.Clear(); Colors.Clear(); Textures.Clear(); Minerals.Clear(); Descriptions.Clear();
        switch (CurrentKind)
        {
            case Kind.Litho: foreach (var x in db.LithoCodes.OrderBy(x => x.Code)) Litho.Add(x); break;
            case Kind.Color: foreach (var x in db.ColorCodes.OrderBy(x => x.Code)) Colors.Add(x); break;
            case Kind.Texture: foreach (var x in db.TextureCodes.OrderBy(x => x.Code)) Textures.Add(x); break;
            case Kind.Mineral: foreach (var x in db.MineralCodes.OrderBy(x => x.Code)) Minerals.Add(x); break;
            case Kind.Description: foreach (var x in db.DescriptionTemplates.OrderBy(x => x.Text)) Descriptions.Add(x); break;
        }
    }

    [RelayCommand]
    void Add()
    {
        switch (CurrentKind)
        {
            case Kind.Litho:
                Litho.Add(new LithoCode { Code = (Litho.Count == 0 ? 1 : Litho.Max(x => x.Code) + 1), Name = "Yangi", HexColor = "#CCCCCC" }); break;
            case Kind.Color:
                Colors.Add(new ColorCode { Code = (Colors.Count == 0 ? 1 : Colors.Max(x => x.Code) + 1), Name = "Yangi", HexColor = "#CCCCCC" }); break;
            case Kind.Texture:
                Textures.Add(new TextureCode { Code = (Textures.Count == 0 ? 1 : Textures.Max(x => x.Code) + 1), Name = "Yangi" }); break;
            case Kind.Mineral:
                Minerals.Add(new MineralCode { Code = (Minerals.Count == 0 ? 1 : Minerals.Max(x => x.Code) + 1), Name = "Yangi" }); break;
            case Kind.Description:
                Descriptions.Add(new DescriptionTemplate { Text = "Yangi tavsif" }); break;
        }
    }

    [RelayCommand]
    void Delete()
    {
        if (Selected == null) return;
        switch (Selected)
        {
            case LithoCode l: Litho.Remove(l); break;
            case ColorCode c: Colors.Remove(c); break;
            case TextureCode t: Textures.Remove(t); break;
            case MineralCode m: Minerals.Remove(m); break;
            case DescriptionTemplate d: Descriptions.Remove(d); break;
        }
    }

    [RelayCommand]
    void Save()
    {
        using var db = new AppDbContext();
        switch (CurrentKind)
        {
            case Kind.Litho: Sync(db.LithoCodes, Litho, x => x.Id); break;
            case Kind.Color: Sync(db.ColorCodes, Colors, x => x.Id); break;
            case Kind.Texture: Sync(db.TextureCodes, Textures, x => x.Id); break;
            case Kind.Mineral: Sync(db.MineralCodes, Minerals, x => x.Id); break;
            case Kind.Description: Sync(db.DescriptionTemplates, Descriptions, x => x.Id); break;
        }
        db.SaveChanges();
        RefCache.Instance.Reload();
        AppState.Instance.RaiseDataChanged();
        MessageBox.Show("Spravochnik saqlandi.", "GeoData Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static void Sync<T>(Microsoft.EntityFrameworkCore.DbSet<T> set,
        System.Collections.Generic.IEnumerable<T> items,
        System.Func<T, int> id) where T : class
    {
        var list = items.ToList();
        var keep = list.Where(x => id(x) != 0).Select(id).ToHashSet();
        foreach (var g in set.ToList().Where(e => !keep.Contains(id(e)))) set.Remove(g);
        foreach (var x in list)
        {
            if (id(x) == 0) set.Add(x); else set.Update(x);
        }
    }
}
