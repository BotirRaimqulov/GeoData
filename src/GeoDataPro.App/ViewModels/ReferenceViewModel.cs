using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoDataPro.Core.Data;
using GeoDataPro.App.Services;
using GeoDataPro.Core.Security;
using GeoDataPro.Core.Services;
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

    public bool CanEdit => AppState.Instance.Can(Permissions.ReferenceWrite);

    public void Load()
    {
        Litho.Clear(); Colors.Clear(); Textures.Clear(); Minerals.Clear(); FloraFauna.Clear();
        IronHydroxides.Clear(); ClasticMaterials.Clear(); Descriptions.Clear();
        HasUnsaved = false;

        var snapshot = AppState.Instance.Data.GetReferences();
        switch (CurrentKind)
        {
            case Kind.Litho: foreach (var x in snapshot.Litho) Litho.Add(x); break;
            case Kind.Color: foreach (var x in snapshot.Colors) Colors.Add(x); break;
            case Kind.Texture: foreach (var x in snapshot.Textures) Textures.Add(x); break;
            case Kind.Mineral: foreach (var x in snapshot.Minerals) Minerals.Add(x); break;
            case Kind.FloraFauna: foreach (var x in snapshot.FloraFauna) FloraFauna.Add(x); break;
            case Kind.IronHydroxide: foreach (var x in snapshot.IronHydroxides) IronHydroxides.Add(x); break;
            case Kind.ClasticMaterial: foreach (var x in snapshot.ClasticMaterials) ClasticMaterials.Add(x); break;
            case Kind.Description: foreach (var x in snapshot.Descriptions) Descriptions.Add(x); break;
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

        try
        {
            AppState.Instance.Data.SaveReferences(MapKind(CurrentKind), CurrentItems());
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

    static ReferenceKind MapKind(Kind kind) => kind switch
    {
        Kind.Litho => ReferenceKind.Litho,
        Kind.Color => ReferenceKind.Color,
        Kind.Texture => ReferenceKind.Texture,
        Kind.Mineral => ReferenceKind.Mineral,
        Kind.FloraFauna => ReferenceKind.FloraFauna,
        Kind.IronHydroxide => ReferenceKind.IronHydroxide,
        Kind.ClasticMaterial => ReferenceKind.ClasticMaterial,
        _ => ReferenceKind.Description,
    };

    System.Collections.Generic.List<object> CurrentItems() => CurrentKind switch
    {
        Kind.Litho => Litho.Cast<object>().ToList(),
        Kind.Color => Colors.Cast<object>().ToList(),
        Kind.Texture => Textures.Cast<object>().ToList(),
        Kind.Mineral => Minerals.Cast<object>().ToList(),
        Kind.FloraFauna => FloraFauna.Cast<object>().ToList(),
        Kind.IronHydroxide => IronHydroxides.Cast<object>().ToList(),
        Kind.ClasticMaterial => ClasticMaterials.Cast<object>().ToList(),
        _ => Descriptions.Cast<object>().ToList(),
    };

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