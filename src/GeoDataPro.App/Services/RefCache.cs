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
    public List<Zone> Zones { get; private set; } = new();
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
        Zones = db.Zones.OrderBy(x => x.Id).ToList();
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

    public static RefCache Instance { get; } = new();
}
