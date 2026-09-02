using CommunityToolkit.Mvvm.ComponentModel;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

/// <summary>Dala jurnali jadvalidagi bitta qatorning tahrirlanadigan modeli.</summary>
public partial class JournalRowVm : ObservableObject
{
    /// <summary>Donadorlik uchun ruxsat etilgan qiymatlar (Kern tavsifi popup'ida ham ishlatiladi).</summary>
    public static readonly string[] GrainSizes = { "mayda", "o'rta", "yirik" };

    public JournalRow Model { get; }

    bool _ready;
    /// <summary>Oxirgi marta avtomatik yozilgan matn — foydalanuvchi tahririni ajratish uchun.</summary>
    string _lastAuto = "";

    public JournalRowVm(JournalRow model)
    {
        Model = model;
        _top = model.Top;
        _bottom = model.Bottom;
        _coreRecoveryM = model.CoreRecoveryM;
        _zoneName = model.ZoneName;
        _lithoCode = model.LithoCode;
        _colorCode = model.ColorCode;
        _textureCode = model.TextureCode;
        _mineralCode = model.MineralCode;
        _grainSize = model.GrainSize;
        _hardness = model.Hardness;
        _carbonateCo2 = model.CarbonateCo2;
        _description = model.Description;
        _lastAuto = BuildAutoDescription();
        // Tavsif bo'sh yoki hozirgi avto-natijaga teng bo'lsa — avto rejimda.
        _descriptionIsAuto = string.IsNullOrWhiteSpace(model.Description)
                             || string.Equals(model.Description?.Trim(), _lastAuto, System.StringComparison.OrdinalIgnoreCase);
        _ready = true;
    }

    /// <summary>Load paytida kiritilgan qatorni "yangi" deb belgilash uchun.</summary>
    public void MarkNew() => Touch();

    public int OrderNo
    {
        get => Model.OrderNo;
        set { Model.OrderNo = value; OnPropertyChanged(); }
    }

    [ObservableProperty] private double _top;
    [ObservableProperty] private double _bottom;
    [ObservableProperty] private double _coreRecoveryM;
    [ObservableProperty] private string? _zoneName;
    [ObservableProperty] private int? _lithoCode;
    [ObservableProperty] private int? _colorCode;
    [ObservableProperty] private int? _textureCode;
    [ObservableProperty] private int? _mineralCode;
    /// <summary>Donadorlik: "mayda" / "o'rta" / "yirik" yoki bo'sh.</summary>
    [ObservableProperty] private string? _grainSize;
    [ObservableProperty] private double? _hardness;
    [ObservableProperty] private double? _carbonateCo2;
    [ObservableProperty] private string? _description;

    public bool IsDirty { get; private set; }

    partial void OnTopChanged(double value) { Model.Top = value; Touch(); Recalc(); }
    partial void OnBottomChanged(double value) { Model.Bottom = value; Touch(); Recalc(); }
    partial void OnCoreRecoveryMChanged(double value) { Model.CoreRecoveryM = value; Touch(); Recalc(); }
    partial void OnZoneNameChanged(string? value) { Model.ZoneName = value; Touch(); }
    partial void OnLithoCodeChanged(int? value) { Model.LithoCode = value; Touch(); OnPropertyChanged(nameof(LithoDisplay)); OnPropertyChanged(nameof(LithoPattern)); AutoFillDescription(); }
    partial void OnColorCodeChanged(int? value) { Model.ColorCode = value; Touch(); OnPropertyChanged(nameof(ColorDisplay)); OnPropertyChanged(nameof(ColorHex)); AutoFillDescription(); }
    partial void OnTextureCodeChanged(int? value) { Model.TextureCode = value; Touch(); OnPropertyChanged(nameof(TextureDisplay)); AutoFillDescription(); }
    partial void OnMineralCodeChanged(int? value) { Model.MineralCode = value; Touch(); OnPropertyChanged(nameof(MineralDisplay)); AutoFillDescription(); }
    partial void OnGrainSizeChanged(string? value) { Model.GrainSize = value; Touch(); AutoFillDescription(); }
    partial void OnHardnessChanged(double? value) { Model.Hardness = value; Touch(); }
    partial void OnCarbonateCo2Changed(double? value) { Model.CarbonateCo2 = value; Touch(); }

    bool _suppressDescNotify;
    partial void OnDescriptionChanged(string? value)
    {
        Model.Description = value;
        Touch();
        // Dasturiy o'zgarish emas, ya'ni foydalanuvchi qo'lda yozdi.
        if (!_suppressDescNotify)
        {
            var v = value?.Trim() ?? "";
            // Bo'sh yoki hozirgi/oxirgi avto-matnga teng bo'lsa — avto rejim davom etadi.
            DescriptionIsAuto = v.Length == 0
                                || string.Equals(v, _lastAuto, System.StringComparison.OrdinalIgnoreCase)
                                || string.Equals(v, BuildAutoDescription(), System.StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- Avtomatik tavsif ----------

    [ObservableProperty] private bool _descriptionIsAuto = true;

    partial void OnDescriptionIsAutoChanged(bool value) { }

    /// <summary>
    /// Litho + rang + tekstura + mineralizatsiya + donadorlik kombinatsiyasiga eng mos
    /// tayyor shablonni qidiradi (qancha ko'p maydon tanlangan bo'lsa, shuncha aniqroq
    /// shablon topiladi). Mos shablon topilmasa, nomlarni oddiy birlashtirib yozadi.
    /// </summary>
    public string BuildAutoDescription()
    {
        var r = RefCache.Instance;

        var template = r.BestTemplate(LithoCode, ColorCode, TextureCode, MineralCode, GrainSize);
        if (template != null) return template.Text;

        var parts = new System.Collections.Generic.List<string>();

        var litho = r.Litho4(LithoCode)?.Name;
        var color = r.Color4(ColorCode)?.Name;
        var head = string.Join(" ", new[] { litho, Lower(color) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(head)) parts.Add(head);

        if (!string.IsNullOrWhiteSpace(GrainSize)) parts.Add($"{Lower(GrainSize)} donador");

        var texture = r.Texture4(TextureCode)?.Name;
        if (!string.IsNullOrWhiteSpace(texture)) parts.Add(Lower(texture)!);

        var mineral = r.Mineral4(MineralCode)?.Name;
        if (!string.IsNullOrWhiteSpace(mineral)) parts.Add(Lower(mineral)!);

        return string.Join(", ", parts);

        static string? Lower(string? s) =>
            string.IsNullOrWhiteSpace(s) ? s
            : char.ToLowerInvariant(s![0]) + s.Substring(1);
    }

    void AutoFillDescription()
    {
        if (!_ready) return;
        var text = BuildAutoDescription();

        // Avto rejimda emas bo'lsa ham: hozirgi matn oldingi avto-natijaga teng bo'lsa,
        // demak foydalanuvchi hech narsa yozmagan — avtoni yangilaymiz.
        var cur = (Description ?? "").Trim();
        bool followAuto = DescriptionIsAuto
                          || cur.Length == 0
                          || string.Equals(cur, _lastAuto, System.StringComparison.OrdinalIgnoreCase);

        _lastAuto = text;
        if (!followAuto) return;
        if (text == (Description ?? "")) { DescriptionIsAuto = true; return; }

        _suppressDescNotify = true;
        Description = text;
        DescriptionIsAuto = true;
        _suppressDescNotify = false;
    }

    /// <summary>"Avtomatik tavsifga qaytarish" tugmasi uchun.</summary>
    public void RegenerateDescription()
    {
        var text = BuildAutoDescription();
        _lastAuto = text;
        _suppressDescNotify = true;
        Description = text;
        DescriptionIsAuto = true;
        _suppressDescNotify = false;
    }

    void Touch() { if (!_ready) return; IsDirty = true; DirtyChanged?.Invoke(); }
    public event System.Action? DirtyChanged;
    public void ClearDirty() => IsDirty = false;

    void Recalc()
    {
        OnPropertyChanged(nameof(Interval));
        OnPropertyChanged(nameof(RecoveryPercent));
        OnPropertyChanged(nameof(RecoveryText));
    }

    public double Interval => System.Math.Round(Bottom - Top, 3);
    public double RecoveryPercent => Interval > 0 ? System.Math.Round(CoreRecoveryM / Interval * 100, 1) : 0;
    public string RecoveryText => $"{CoreRecoveryM:0.##} m ({RecoveryPercent:0.#}%)";

    public string LithoDisplay
    {
        get
        {
            var l = RefCache.Instance.Litho4(LithoCode);
            return l == null ? "" : $"{l.Code}  {l.Name}";
        }
    }
    public string? LithoPattern => RefCache.Instance.Litho4(LithoCode)?.PatternKey;

    public string ColorDisplay
    {
        get
        {
            var c = RefCache.Instance.Color4(ColorCode);
            return c == null ? "" : $"{c.Code}  {c.Name}";
        }
    }
    public string ColorHex => RefCache.Instance.Color4(ColorCode)?.HexColor ?? "#00000000";

    public string TextureDisplay => RefCache.Instance.Texture4(TextureCode)?.Name ?? "";
    public string MineralDisplay => RefCache.Instance.Mineral4(MineralCode)?.Name ?? "";
}
