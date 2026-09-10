using System;
using System.Collections.Generic;
using System.Linq;
using GeoDataPro.Core.Data;
using GeoDataPro.Core.Security;

namespace GeoDataPro.Core.Services;

public class RefCache
{
    IGeoDataService? _source;

    public void Attach(IGeoDataService source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public List<LithoCode> Litho { get; private set; } = new();
    public List<ColorCode> Colors { get; private set; } = new();
    public List<TextureCode> Textures { get; private set; } = new();
    public List<MineralCode> Minerals { get; private set; } = new();
    public List<FloraFaunaCode> FloraFauna { get; private set; } = new();
    public List<IronHydroxideCode> IronHydroxides { get; private set; } = new();
    public List<ClasticMaterialCode> ClasticMaterials { get; private set; } = new();
    public List<DescriptionTemplate> Descriptions { get; private set; } = new();

    Dictionary<int, LithoCode> _litho = new();
    Dictionary<int, ColorCode> _color = new();
    Dictionary<int, TextureCode> _texture = new();
    Dictionary<int, MineralCode> _mineral = new();
    Dictionary<int, FloraFaunaCode> _floraFauna = new();
    Dictionary<int, IronHydroxideCode> _ironHydroxide = new();
    Dictionary<int, ClasticMaterialCode> _clasticMaterial = new();

    public void Reload()
    {
        if (_source == null) return;

        ReferenceSnapshot snapshot;
        try
        {
            snapshot = _source.GetReferences();
        }
        catch (SecurityDeniedException)
        {
            return;
        }

        Litho = snapshot.Litho.ToList();
        Colors = snapshot.Colors.ToList();
        Textures = snapshot.Textures.ToList();
        Minerals = snapshot.Minerals.ToList();
        FloraFauna = snapshot.FloraFauna.ToList();
        IronHydroxides = snapshot.IronHydroxides.ToList();
        ClasticMaterials = snapshot.ClasticMaterials.ToList();
        Descriptions = snapshot.Descriptions.ToList();

        _litho = Distinct(Litho, x => x.Code);
        _color = Distinct(Colors, x => x.Code);
        _texture = Distinct(Textures, x => x.Code);
        _mineral = Distinct(Minerals, x => x.Code);
        _floraFauna = Distinct(FloraFauna, x => x.Code);
        _ironHydroxide = Distinct(IronHydroxides, x => x.Code);
        _clasticMaterial = Distinct(ClasticMaterials, x => x.Code);
    }

    static Dictionary<int, T> Distinct<T>(IEnumerable<T> items, Func<T, int> key)
    {
        var map = new Dictionary<int, T>();
        foreach (var item in items) map[key(item)] = item;
        return map;
    }

    public LithoCode? Litho4(int? code) => code is int c && _litho.TryGetValue(c, out var v) ? v : null;
    public ColorCode? Color4(int? code) => code is int c && _color.TryGetValue(c, out var v) ? v : null;
    public TextureCode? Texture4(int? code) => code is int c && _texture.TryGetValue(c, out var v) ? v : null;
    public MineralCode? Mineral4(int? code) => code is int c && _mineral.TryGetValue(c, out var v) ? v : null;
    public FloraFaunaCode? FloraFauna4(int? code) => code is int c && _floraFauna.TryGetValue(c, out var v) ? v : null;
    public IronHydroxideCode? IronHydroxide4(int? code) => code is int c && _ironHydroxide.TryGetValue(c, out var v) ? v : null;
    public ClasticMaterialCode? ClasticMaterial4(int? code) => code is int c && _clasticMaterial.TryGetValue(c, out var v) ? v : null;

    /// <summary>
    /// Litho/rang/tekstura/mineral/donadorlik kombinatsiyasiga eng mos shablonni topadi.
    /// Shablonning to'ldirilgan har bir maydoni joriy qiymatga mos bo'lishi shart (bo'sh
    /// maydon — joker). Bir nechta shablon mos kelsa, eng ko'p maydoni to'ldirilgani
    /// (eng aniqrog'i) g'olib bo'ladi. Hech bir maydoni bog'lanmagan (hammasi bo'sh)
    /// shablonlar hech qachon moslik sifatida qaytarilmaydi.
    /// </summary>
    public DescriptionTemplate? BestTemplate(int? litho, int? color, int? texture, int? mineral, string? grainSize)
    {
        DescriptionTemplate? best = null;
        int bestScore = 0;
        foreach (var t in Descriptions)
        {
            if (t.LithoCode is int tl && tl != litho) continue;
            if (t.ColorCode is int tc && tc != color) continue;
            if (t.TextureCode is int tt && tt != texture) continue;
            if (t.MineralCode is int tm && tm != mineral) continue;
            if (!string.IsNullOrEmpty(t.GrainSize) &&
                !string.Equals(t.GrainSize, grainSize, System.StringComparison.OrdinalIgnoreCase)) continue;

            int score = (t.LithoCode.HasValue ? 1 : 0) + (t.ColorCode.HasValue ? 1 : 0)
                      + (t.TextureCode.HasValue ? 1 : 0) + (t.MineralCode.HasValue ? 1 : 0)
                      + (!string.IsNullOrEmpty(t.GrainSize) ? 1 : 0);
            if (score == 0) continue;
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    public static RefCache Instance { get; } = new();
}
