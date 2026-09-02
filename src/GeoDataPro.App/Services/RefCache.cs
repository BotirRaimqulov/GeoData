using System.Collections.Generic;
using System.Linq;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Services;

/// <summary>Spravochniklarni bir marta yuklab, ID -> obyekt lug'atlarini beradi.</summary>
public class RefCache
{
    public List<LithoCode> Litho { get; private set; } = new();
    public List<ColorCode> Colors { get; private set; } = new();
    public List<TextureCode> Textures { get; private set; } = new();
    public List<MineralCode> Minerals { get; private set; } = new();
    public List<DescriptionTemplate> Descriptions { get; private set; } = new();

    Dictionary<int, LithoCode> _litho = new();
    Dictionary<int, ColorCode> _color = new();
    Dictionary<int, TextureCode> _texture = new();
    Dictionary<int, MineralCode> _mineral = new();

    public void Reload()
    {
        using var db = new AppDbContext();
        Litho = db.LithoCodes.OrderBy(x => x.Code).ToList();
        Colors = db.ColorCodes.OrderBy(x => x.Code).ToList();
        Textures = db.TextureCodes.OrderBy(x => x.Code).ToList();
        Minerals = db.MineralCodes.OrderBy(x => x.Code).ToList();
        Descriptions = db.DescriptionTemplates.OrderBy(x => x.Text).ToList();

        _litho = Litho.ToDictionary(x => x.Code);
        _color = Colors.ToDictionary(x => x.Code);
        _texture = Textures.ToDictionary(x => x.Code);
        _mineral = Minerals.ToDictionary(x => x.Code);
    }

    public LithoCode? Litho4(int? code) => code is int c && _litho.TryGetValue(c, out var v) ? v : null;
    public ColorCode? Color4(int? code) => code is int c && _color.TryGetValue(c, out var v) ? v : null;
    public TextureCode? Texture4(int? code) => code is int c && _texture.TryGetValue(c, out var v) ? v : null;
    public MineralCode? Mineral4(int? code) => code is int c && _mineral.TryGetValue(c, out var v) ? v : null;

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
