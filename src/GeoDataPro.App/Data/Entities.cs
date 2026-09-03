using System.Collections.Generic;

namespace GeoDataPro.App.Data;

/// <summary>Loyiha (masalan "Loyiha-01").</summary>
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? CoordinateX { get; set; }
    public string? CoordinateY { get; set; }
    public string? CoordinateH { get; set; }
    public string? Notes { get; set; }

    public List<Well> Wells { get; set; } = new();
}

/// <summary>Skvazhina / quduq.</summary>
public class Well
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Quduq raqami, masalan "1001".</summary>
    public string Number { get; set; } = "";
    public string? RigNumber { get; set; }          // Burg'ilash Usk №
    public double? StartDepth { get; set; }
    public double? EndDepth { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Geologist { get; set; }
    public string? Notes { get; set; }

    public List<JournalRow> JournalRows { get; set; } = new();
    public List<SampleRow> SampleRows { get; set; } = new();
    public List<SrpRow> SrpRows { get; set; } = new();
}

/// <summary>Litologik kod spravochnigi.</summary>
public class LithoCode
{
    public int Id { get; set; }
    public int Code { get; set; }
    /// <summary>O'zbekcha nomi.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ruscha nomi (наименование породы).</summary>
    public string? NameRu { get; set; }
    public string? StratCode { get; set; }
    /// <summary>Fayl nomi (assets/litho/*.png) yoki HEX rang.</summary>
    public string? PatternKey { get; set; }
    public string? HexColor { get; set; }
}

/// <summary>Kern rangi spravochnigi.</summary>
public class ColorCode
{
    public int Id { get; set; }
    public int Code { get; set; }
    /// <summary>O'zbekcha nomi.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ruscha nomi (цвет).</summary>
    public string? NameRu { get; set; }
    public string HexColor { get; set; } = "#B0B0B0";
}

/// <summary>Tekstura spravochnigi.</summary>
public class TextureCode
{
    public int Id { get; set; }
    public int Code { get; set; }
    /// <summary>O'zbekcha nomi.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ruscha nomi (текстура).</summary>
    public string? NameRu { get; set; }
    public string? PatternKey { get; set; }
}

/// <summary>Mineralizatsiya spravochnigi.</summary>
public class MineralCode
{
    public int Id { get; set; }
    public int Code { get; set; }
    /// <summary>O'zbekcha nomi.</summary>
    public string Name { get; set; } = "";
    /// <summary>Ruscha nomi (минерализация / включения).</summary>
    public string? NameRu { get; set; }
    public string? PatternKey { get; set; }
}

/// <summary>
/// Tasnif / description shabloni. Litho/rang/tekstura/mineral/donadorlik maydonlari
/// ixtiyoriy — to'ldirilgan maydon shu qiymatga mos bo'lishi shart, bo'sh maydon esa
/// "joker" (istalgan qiymatga mos) hisoblanadi. Qancha ko'p maydon to'ldirilgan bo'lsa,
/// shuncha aniqroq (o'ziga xos) shablon hisoblanadi — moslik qidirishda ustunlik beriladi.
/// </summary>
public class DescriptionTemplate
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int? LithoCode { get; set; }
    public int? ColorCode { get; set; }
    public int? TextureCode { get; set; }
    public int? MineralCode { get; set; }
    /// <summary>Donadorlik: "mayda" / "o'rta" / "yirik" yoki bo'sh.</summary>
    public string? GrainSize { get; set; }
}

/// <summary>Dala jurnali qatori.</summary>
public class JournalRow
{
    public int Id { get; set; }
    public int WellId { get; set; }
    public Well? Well { get; set; }

    public int OrderNo { get; set; }
    public double Top { get; set; }
    public double Bottom { get; set; }
    /// <summary>Kern chiqishi (m).</summary>
    public double CoreRecoveryM { get; set; }
    public string? ZoneName { get; set; }
    public int? LithoCode { get; set; }
    public int? ColorCode { get; set; }
    public int? TextureCode { get; set; }
    public int? MineralCode { get; set; }
    /// <summary>Donadorlik: "mayda" / "o'rta" / "yirik" yoki bo'sh.</summary>
    public string? GrainSize { get; set; }
    public double? Hardness { get; set; }              // Qattiqlik toifasi
    public double? CarbonateCo2 { get; set; }          // Karbonatliligi CO2
    public string? Description { get; set; }

    public double Interval => System.Math.Round(Bottom - Top, 3);
    public double RecoveryPercent => Interval > 0 ? System.Math.Round(CoreRecoveryM / Interval * 100, 1) : 0;
}

/// <summary>Namuna (образец) qatori.</summary>
public class SampleRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Id { get; set; }
    public int WellId { get; set; }
    public Well? Well { get; set; }

    string _sampleNumber = "";
    int? _sampleTypeCode;
    double _top;
    double _bottom;
    string? _zoneName;
    string? _notes;
    int _displayOrder;

    public string SampleNumber
    {
        get => _sampleNumber;
        set => SetProperty(ref _sampleNumber, value);
    }

    public int? SampleTypeCode
    {
        get => _sampleTypeCode;
        set => SetProperty(ref _sampleTypeCode, value);
    }

    public double Top
    {
        get => _top;
        set
        {
            if (SetProperty(ref _top, value))
                OnPropertyChanged(nameof(Length));
        }
    }

    public double Bottom
    {
        get => _bottom;
        set
        {
            if (SetProperty(ref _bottom, value))
                OnPropertyChanged(nameof(Length));
        }
    }

    public string? ZoneName
    {
        get => _zoneName;
        set => SetProperty(ref _zoneName, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    public double Length => System.Math.Round(Bottom - Top, 3);
}

/// <summary>SRP - kern bo'yicha gamma-karotaj (Core_GK) nuqtasi.</summary>
public class SrpRow
{
    public int Id { get; set; }
    public int WellId { get; set; }
    public Well? Well { get; set; }

    public double Md { get; set; }
    public double CoreGk { get; set; }
}
