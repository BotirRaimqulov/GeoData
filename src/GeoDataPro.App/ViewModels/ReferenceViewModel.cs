using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    public enum Kind { Litho, Color, Texture, Mineral, FloraFauna, IronHydroxide, ClasticMaterial, Description }
    public Kind CurrentKind { get; }

    public ObservableCollection<LithoCode> Litho { get; } = new();
    public ObservableCollection<ColorCode> Colors { get; } = new();
    public ObservableCollection<TextureCode> Textures { get; } = new();
    public ObservableCollection<MineralCode> Minerals { get; } = new();
    public ObservableCollection<FloraFaunaCode> FloraFauna { get; } = new();
    public ObservableCollection<IronHydroxideCode> IronHydroxides { get; } = new();
    public ObservableCollection<ClasticMaterialCode> ClasticMaterials { get; } = new();
    public ObservableCollection<DescriptionTemplate> Descriptions { get; } = new();

    /// <summary>Jadvalda ko'p tanlangan qatorlar (Ctrl+Click / Shift+Click orqali).</summary>
    public ObservableCollection<object> SelectedItems { get; } = new();

    /// <summary>Tavsif shablonlarini Litho/Rang/Tekstura/Mineral bilan bog'lash uchun (spravochnik ro'yxatlari).</summary>
    public RefCache Ref => RefCache.Instance;

    [ObservableProperty] private object? _selected;
    [ObservableProperty] private bool _hasUnsaved;
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
            Kind.FloraFauna => "Flora-Fauna",
            Kind.IronHydroxide => "Gidrookisleniya",
            Kind.ClasticMaterial => "Mineral tarkibi",
            _ => "Tavsif shablonlari",
        };
        Litho.CollectionChanged += Collection_Changed;
        Colors.CollectionChanged += Collection_Changed;
        Textures.CollectionChanged += Collection_Changed;
        Minerals.CollectionChanged += Collection_Changed;
        FloraFauna.CollectionChanged += Collection_Changed;
        IronHydroxides.CollectionChanged += Collection_Changed;
        ClasticMaterials.CollectionChanged += Collection_Changed;
        Descriptions.CollectionChanged += Collection_Changed;
        Load();
    }

    void Collection_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
            HasUnsaved = true;
    }

    public void Load()
    {
        using var db = new AppDbContext();
        Litho.Clear(); Colors.Clear(); Textures.Clear(); Minerals.Clear(); FloraFauna.Clear();
        IronHydroxides.Clear(); ClasticMaterials.Clear(); Descriptions.Clear();
        HasUnsaved = false;
        switch (CurrentKind)
        {
            case Kind.Litho: foreach (var x in db.LithoCodes.AsNoTracking().OrderBy(x => x.Code)) Litho.Add(x); break;
            case Kind.Color: foreach (var x in db.ColorCodes.AsNoTracking().OrderBy(x => x.Code)) Colors.Add(x); break;
            case Kind.Texture: foreach (var x in db.TextureCodes.AsNoTracking().OrderBy(x => x.Code)) Textures.Add(x); break;
            case Kind.Mineral: foreach (var x in db.MineralCodes.AsNoTracking().OrderBy(x => x.Code)) Minerals.Add(x); break;
            case Kind.FloraFauna: foreach (var x in db.FloraFaunaCodes.AsNoTracking().OrderBy(x => x.Code)) FloraFauna.Add(x); break;
            case Kind.IronHydroxide: foreach (var x in db.IronHydroxideCodes.AsNoTracking().OrderBy(x => x.Code)) IronHydroxides.Add(x); break;
            case Kind.ClasticMaterial: foreach (var x in db.ClasticMaterialCodes.AsNoTracking().OrderBy(x => x.Code)) ClasticMaterials.Add(x); break;
            case Kind.Description: foreach (var x in db.DescriptionTemplates.AsNoTracking().OrderBy(x => x.Text)) Descriptions.Add(x); break;
        }
        // Add paytida HasUnsaved true bo'lib qoladi — load holatida uni tozalaymiz.
        HasUnsaved = false;
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
            case Kind.FloraFauna:
                FloraFauna.Add(new FloraFaunaCode { Code = (FloraFauna.Count == 0 ? 1 : FloraFauna.Max(x => x.Code) + 1), Name = "Yangi" }); break;
            case Kind.IronHydroxide:
                IronHydroxides.Add(new IronHydroxideCode { Code = (IronHydroxides.Count == 0 ? 1 : IronHydroxides.Max(x => x.Code) + 1), Name = "Yangi" }); break;
            case Kind.ClasticMaterial:
                ClasticMaterials.Add(new ClasticMaterialCode { Code = (ClasticMaterials.Count == 0 ? 1 : ClasticMaterials.Max(x => x.Code) + 1), Name = "Yangi" }); break;
            case Kind.Description:
                Descriptions.Add(new DescriptionTemplate { Text = "Yangi tavsif" }); break;
        }
    }

    [RelayCommand]
    void Delete()
    {
        // Ko'p tanlangan elementlar bo'lsa — hammasini o'chiramiz.
        var toDelete = SelectedItems.Count > 1
            ? SelectedItems.ToList()
            : (Selected != null ? new System.Collections.Generic.List<object> { Selected } : new System.Collections.Generic.List<object>());

        if (toDelete.Count == 0) return;

        string msg = toDelete.Count == 1
            ? "Tanlangan elementni o'chirasizmi?"
            : $"Tanlangan {toDelete.Count} ta elementni o'chirasizmi?";

        if (MessageBox.Show(msg, "Tasdiqlash",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        SelectedItems.Clear();
        foreach (var item in toDelete)
        {
            switch (item)
            {
                case LithoCode l: Litho.Remove(l); break;
                case ColorCode c: Colors.Remove(c); break;
                case TextureCode t: Textures.Remove(t); break;
                case MineralCode m: Minerals.Remove(m); break;
                case FloraFaunaCode f: FloraFauna.Remove(f); break;
                case IronHydroxideCode ih: IronHydroxides.Remove(ih); break;
                case ClasticMaterialCode cm: ClasticMaterials.Remove(cm); break;
                case DescriptionTemplate d: Descriptions.Remove(d); break;
            }
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
                case Kind.FloraFauna: Sync(db, db.FloraFaunaCodes, FloraFauna, x => x.Id); break;
                case Kind.IronHydroxide: Sync(db, db.IronHydroxideCodes, IronHydroxides, x => x.Id); break;
                case Kind.ClasticMaterial: Sync(db, db.ClasticMaterialCodes, ClasticMaterials, x => x.Id); break;
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
        HasUnsaved = false;
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
            case Kind.FloraFauna:
                return ValidateCodes(FloraFauna.Select(x => (x.Code, x.Name)), "flora-fauna kodi", out message);
            case Kind.IronHydroxide:
                return ValidateCodes(IronHydroxides.Select(x => (x.Code, x.Name)), "gidrookisleniya kodi", out message);
            case Kind.ClasticMaterial:
                return ValidateCodes(ClasticMaterials.Select(x => (x.Code, x.Name)), "mineral tarkibi kodi", out message);
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