using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>Tavsif shablonlarini Litho/Rang/Tekstura/Mineral bilan bog'lash uchun (spravochnik ro'yxatlari).</summary>
    public RefCache Ref => RefCache.Instance;

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
            case Kind.Litho: foreach (var x in db.LithoCodes.AsNoTracking().OrderBy(x => x.Code)) Litho.Add(x); break;
            case Kind.Color: foreach (var x in db.ColorCodes.AsNoTracking().OrderBy(x => x.Code)) Colors.Add(x); break;
            case Kind.Texture: foreach (var x in db.TextureCodes.AsNoTracking().OrderBy(x => x.Code)) Textures.Add(x); break;
            case Kind.Mineral: foreach (var x in db.MineralCodes.AsNoTracking().OrderBy(x => x.Code)) Minerals.Add(x); break;
            case Kind.Description: foreach (var x in db.DescriptionTemplates.AsNoTracking().OrderBy(x => x.Text)) Descriptions.Add(x); break;
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
        if (!Validate(out var validationError))
        {
            AppNotifier.Warn(validationError);
            return;
        }

        using var db = new AppDbContext();
        try
        {
            switch (CurrentKind)
            {
                case Kind.Litho: Sync(db, db.LithoCodes, Litho, x => x.Id); break;
                case Kind.Color: Sync(db, db.ColorCodes, Colors, x => x.Id); break;
                case Kind.Texture: Sync(db, db.TextureCodes, Textures, x => x.Id); break;
                case Kind.Mineral: Sync(db, db.MineralCodes, Minerals, x => x.Id); break;
                case Kind.Description: Sync(db, db.DescriptionTemplates, Descriptions, x => x.Id); break;
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            AppNotifier.Error("Spravochnikni saqlab bo'lmadi.", ex);
            return;
        }

        RefCache.Instance.Reload();
        AppState.Instance.RaiseDataChanged();
        AppNotifier.Info("Spravochnik saqlandi.");
    }

    static void Sync<T>(AppDbContext db,
        Microsoft.EntityFrameworkCore.DbSet<T> set,
        System.Collections.Generic.IEnumerable<T> items,
        System.Func<T, int> id) where T : class
    {
        var list = items.ToList();
        var existing = set.ToList();
        var existingById = existing.Where(x => id(x) != 0).ToDictionary(id);
        var keep = list.Where(x => id(x) != 0).Select(id).ToHashSet();
        foreach (var g in existing.Where(e => !keep.Contains(id(e)))) set.Remove(g);
        foreach (var x in list)
        {
            var entityId = id(x);
            if (entityId == 0) set.Add(x);
            else if (existingById.TryGetValue(entityId, out var tracked)) db.Entry(tracked).CurrentValues.SetValues(x);
            else set.Update(x);
        }
    }

    bool Validate(out string message)
    {
        switch (CurrentKind)
        {
            case Kind.Litho:
                return ValidateCodes(Litho.Select(x => (x.Code, x.Name)), "litologik kod", out message);
            case Kind.Color:
                return ValidateCodes(Colors.Select(x => (x.Code, x.Name)), "rang kodi", out message);
            case Kind.Texture:
                return ValidateCodes(Textures.Select(x => (x.Code, x.Name)), "tekstura kodi", out message);
            case Kind.Mineral:
                return ValidateCodes(Minerals.Select(x => (x.Code, x.Name)), "mineral kodi", out message);
            case Kind.Description:
                if (Descriptions.Any(x => string.IsNullOrWhiteSpace(x.Text)))
                {
                    message = "Tavsif shabloni matnini bo'sh qoldirmang.";
                    return false;
                }
                break;
        }

        message = string.Empty;
        return true;
    }

    static bool ValidateCodes(System.Collections.Generic.IEnumerable<(int Code, string Name)> items, string label, out string message)
    {
        var seen = new HashSet<int>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                message = $"{label} nomini bo'sh qoldirmang.";
                return false;
            }

            if (!seen.Add(item.Code))
            {
                message = $"{item.Code} {label} takrorlangan.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }
}
