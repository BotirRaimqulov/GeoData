using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GeoDataPro.App.Data;
using GeoDataPro.App.Services;

namespace GeoDataPro.App.ViewModels;

/// <summary>Dala jurnali jadvalidagi bitta qatorning tahrirlanadigan modeli.</summary>
public partial class JournalRowVm : ObservableObject
{
    /// <summary>Donadorlik uchun ruxsat etilgan qiymatlar (Kern tavsifi popup'ida ham ishlatiladi).</summary>
    public static readonly string[] GrainSizes = { "mayda", "o'rta", "yirik" };
    /// <summary>Qattiqligi uchun ruxsat etilgan qiymatlar (Kern tavsifi popup'ida ham ishlatiladi).</summary>
    public static readonly string[] Hardnesses = { "yumshoq", "o'rta", "qattiq" };
    /// <summary>Sementlashuvi uchun ruxsat etilgan qiymatlar (Kern tavsifi popup'ida ham ishlatiladi).</summary>
    public static readonly string[] Cementations = { "sementlashmagan", "zaif sementlangan", "o'rta sementlangan", "kuchli sementlangan" };

    public JournalRow Model { get; }

    bool _ready;
    /// <summary>Oxirgi marta avtomatik yozilgan tavsif matni — foydalanuvchi tahririni ajratish uchun.</summary>
    string _lastAuto = "";
    /// <summary>Oxirgi marta avtomatik yozilgan zona nomi (OrderNo ga bog'liq).</summary>
    string _lastAutoZone = "";
    int? _lastInferredLitho;
    int? _lastInferredColor;
    int? _lastInferredTexture;
    int? _lastInferredMineral;
    string? _lastInferredGrain;

    public JournalRowVm(JournalRow model)
    {
        Model = model;
        _top = model.Top;
        _bottom = model.Bottom;
        _coreRecoveryM = model.CoreRecoveryM;
        _zoneName = model.ZoneName;
        _lithoCode = model.LithoCode;
        _colorCode = model.ColorCode;
        _ironHydroxideCode = model.IronHydroxideCode;
        _composition = model.Composition;
        // Eski bitta kod → yangi multi-kod formati (migratsiya)
        _clasticMaterialCodes = model.ClasticMaterialCodes
            ?? (model.ClasticMaterialCode.HasValue ? model.ClasticMaterialCode.Value.ToString() : null);
        var _selectedClasticCodes = ParseClasticCodes(_clasticMaterialCodes);
        ClasticMaterialItems = new ObservableCollection<CheckableClasticItem>(
            RefCache.Instance.ClasticMaterials.Select(m => new CheckableClasticItem
            {
                Code = m.Code, Name = m.Name,
                IsChecked = _selectedClasticCodes.Contains(m.Code)
            }));
        foreach (var item in ClasticMaterialItems)
            item.PropertyChanged += (_, _) => OnClasticItemChanged();
        _textureCode = model.TextureCode;
        _grainSize = model.GrainSize;
        _hardness = model.Hardness;
        _cementation = model.Cementation;
        _mineralCode = model.MineralCode;
        _floraFaunaCode = model.FloraFaunaCode;
        _description = model.Description;
        _lastAuto = BuildAutoDescription();
        // Tavsif bo'sh yoki hozirgi avto-natijaga teng bo'lsa — avto rejimda.
        _descriptionIsAuto = string.IsNullOrWhiteSpace(model.Description)
                             || string.Equals(model.Description?.Trim(), _lastAuto, System.StringComparison.OrdinalIgnoreCase);

        // Zona nomi — avtomatik tarzda OrderNo (T/R) ga bog'lanadi.
        // Bo'sh yoki OrderNo ga teng bo'lsa — avto rejimda; aks holda qo'lda tahrir qilingan.
        _lastAutoZone = BuildAutoZoneName();
        _zoneNameIsAuto = string.IsNullOrWhiteSpace(model.ZoneName)
                          || string.Equals(model.ZoneName?.Trim(), _lastAutoZone, System.StringComparison.OrdinalIgnoreCase);

        // Agar zona nomi bo'sh bo'lsa — avtomatik to'ldiramiz.
        if (string.IsNullOrWhiteSpace(model.ZoneName))
        {
            _zoneName = _lastAutoZone;
            Model.ZoneName = _lastAutoZone;
        }

        _ready = true;
    }

    /// <summary>Load paytida kiritilgan qatorni "yangi" deb belgilash uchun.</summary>
    public void MarkNew() => Touch();

    public int OrderNo
    {
        get => Model.OrderNo;
        set
        {
            if (Model.OrderNo == value) return;
            Model.OrderNo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrderNoDisplay));
            AutoFillZoneName();
        }
    }

    /// <summary>Jadvalda ko'rsatiladigan T/R (tartib raqami) — faqat o'qish uchun.</summary>
    public string OrderNoDisplay => OrderNo.ToString();

    [ObservableProperty] private double _top;
    [ObservableProperty] private double _bottom;
    [ObservableProperty] private double _coreRecoveryM;
    [ObservableProperty] private string? _zoneName;
    [ObservableProperty] private int? _lithoCode;
    [ObservableProperty] private int? _colorCode;
    [ObservableProperty] private int? _ironHydroxideCode;
    [ObservableProperty] private string? _composition;
    string? _clasticMaterialCodes;
    public string? ClasticMaterialCodes
    {
        get => _clasticMaterialCodes;
        set
        {
            if (_clasticMaterialCodes == value) return;
            _clasticMaterialCodes = value;
            Model.ClasticMaterialCodes = value;
            Touch();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClasticMaterialDisplay));
            AutoFillDescription();
        }
    }

    public ObservableCollection<CheckableClasticItem> ClasticMaterialItems { get; private set; } = null!;

    void OnClasticItemChanged()
    {
        var codes = string.Join(",", ClasticMaterialItems.Where(x => x.IsChecked).Select(x => x.Code));
        _clasticMaterialCodes = codes.Length > 0 ? codes : null;
        Model.ClasticMaterialCodes = _clasticMaterialCodes;
        Touch();
        OnPropertyChanged(nameof(ClasticMaterialCodes));
        OnPropertyChanged(nameof(ClasticMaterialDisplay));
        AutoFillDescription();
    }

    static System.Collections.Generic.HashSet<int> ParseClasticCodes(string? s) =>
        s == null ? new() : s.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                              .Select(x => int.TryParse(x.Trim(), out var v) ? v : 0)
                              .Where(x => x > 0).ToHashSet();
    [ObservableProperty] private int? _textureCode;
    /// <summary>Donadorlik: "mayda" / "o'rta" / "yirik" yoki bo'sh.</summary>
    [ObservableProperty] private string? _grainSize;
    /// <summary>Qattiqligi: "yumshoq" / "o'rta" / "qattiq" yoki bo'sh.</summary>
    [ObservableProperty] private string? _hardness;
    /// <summary>Sementlashuvi darajasi yoki bo'sh.</summary>
    [ObservableProperty] private string? _cementation;
    [ObservableProperty] private int? _mineralCode;
    [ObservableProperty] private int? _floraFaunaCode;
    [ObservableProperty] private string? _description;

    public bool IsDirty { get; private set; }

    partial void OnTopChanged(double value) { Model.Top = value; Touch(); Recalc(); }
    partial void OnBottomChanged(double value) { Model.Bottom = value; Touch(); Recalc(); }
    partial void OnCoreRecoveryMChanged(double value) { Model.CoreRecoveryM = value; Touch(); Recalc(); }
    bool _suppressZoneNotify;
    partial void OnZoneNameChanged(string? value)
    {
        Model.ZoneName = value;
        Touch();
        // Dasturiy o'zgarish emas — foydalanuvchi qo'lda yozdi.
        if (!_suppressZoneNotify)
        {
            var v = value?.Trim() ?? "";
            // Bo'sh yoki hozirgi/oxirgi avto-qiymatga teng bo'lsa — avto rejim davom etadi.
            ZoneNameIsAuto = v.Length == 0
                              || string.Equals(v, _lastAutoZone, System.StringComparison.OrdinalIgnoreCase)
                              || string.Equals(v, BuildAutoZoneName(), System.StringComparison.OrdinalIgnoreCase);

            // Foydalanuvchi zona nomini tozalasa — avtomatik T/R bilan to'ldiramiz.
            if (v.Length == 0)
                AutoFillZoneName();
        }
    }

    // ---------- Zona nomi avtomatik rejimi ----------

    [ObservableProperty] private bool _zoneNameIsAuto = true;

    /// <summary>OrderNo ga asoslangan avto zona nomi (masalan, "5").</summary>
    string BuildAutoZoneName() => OrderNo.ToString();

    /// <summary>
    /// Agar zona nomi avto rejimda bo'lsa (yoki bo'sh yoki avvalgi avto-qiymatga teng bo'lsa),
    /// uni OrderNo ga muvofiq yangilaydi. Aks holda — foydalanuvchi qo'lda yozgan, tegmaymiz.
    /// </summary>
    void AutoFillZoneName()
    {
        if (!_ready) return;
        var auto = BuildAutoZoneName();
        var cur = (ZoneName ?? "").Trim();
        bool followAuto = ZoneNameIsAuto
                          || cur.Length == 0
                          || string.Equals(cur, _lastAutoZone, System.StringComparison.OrdinalIgnoreCase);

        _lastAutoZone = auto;
        if (!followAuto) return;
        if (auto == (ZoneName ?? "")) { ZoneNameIsAuto = true; return; }

        _suppressZoneNotify = true;
        ZoneName = auto;
        ZoneNameIsAuto = true;
        _suppressZoneNotify = false;
    }

    /// <summary>"Avtomatik zona nomiga qaytarish" tugmasi uchun.</summary>
    public void RegenerateZoneName()
    {
        var auto = BuildAutoZoneName();
        _lastAutoZone = auto;
        _suppressZoneNotify = true;
        ZoneName = auto;
        ZoneNameIsAuto = true;
        _suppressZoneNotify = false;
    }
    partial void OnLithoCodeChanged(int? value) { Model.LithoCode = value; Touch(); OnPropertyChanged(nameof(LithoDisplay)); OnPropertyChanged(nameof(LithoPattern)); AutoFillDescription(); }
    partial void OnColorCodeChanged(int? value) { Model.ColorCode = value; Touch(); OnPropertyChanged(nameof(ColorDisplay)); OnPropertyChanged(nameof(ColorHex)); AutoFillDescription(); }
    partial void OnIronHydroxideCodeChanged(int? value) { Model.IronHydroxideCode = value; Touch(); OnPropertyChanged(nameof(IronHydroxideDisplay)); AutoFillDescription(); }
    partial void OnCompositionChanged(string? value) { Model.Composition = value; Touch(); AutoFillDescription(); }
    partial void OnTextureCodeChanged(int? value) { Model.TextureCode = value; Touch(); OnPropertyChanged(nameof(TextureDisplay)); AutoFillDescription(); }
    partial void OnGrainSizeChanged(string? value) { Model.GrainSize = value; Touch(); AutoFillDescription(); }
    partial void OnHardnessChanged(string? value) { Model.Hardness = value; Touch(); AutoFillDescription(); }
    partial void OnCementationChanged(string? value) { Model.Cementation = value; Touch(); AutoFillDescription(); }
    partial void OnMineralCodeChanged(int? value) { Model.MineralCode = value; Touch(); OnPropertyChanged(nameof(MineralDisplay)); AutoFillDescription(); }
    partial void OnFloraFaunaCodeChanged(int? value) { Model.FloraFaunaCode = value; Touch(); OnPropertyChanged(nameof(FloraFaunaDisplay)); AutoFillDescription(); }

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
            ApplyClassificationFromDescription(v);
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

        var texture = r.Texture4(TextureCode)?.Name;
        if (!string.IsNullOrWhiteSpace(texture)) parts.Add(Lower(texture)!);

        if (!string.IsNullOrWhiteSpace(GrainSize)) parts.Add($"{Lower(GrainSize)} donador");
        if (!string.IsNullOrWhiteSpace(Hardness)) parts.Add(Lower(Hardness)!);
        if (!string.IsNullOrWhiteSpace(Cementation)) parts.Add(Lower(Cementation)!);

        var mineral = r.Mineral4(MineralCode)?.Name;
        if (!string.IsNullOrWhiteSpace(mineral)) parts.Add(Lower(mineral)!);

        var floraFauna = r.FloraFauna4(FloraFaunaCode)?.Name;
        if (!string.IsNullOrWhiteSpace(floraFauna)) parts.Add(Lower(floraFauna)!);

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

    void ApplyClassificationFromDescription(string? description)
    {
        var result = DescriptionClassifier.Classify(description, RefCache.Instance);

        TryApplyCode(result.LithoCode, LithoCode, _lastInferredLitho, v => LithoCode = v, v => _lastInferredLitho = v);
        TryApplyCode(result.ColorCode, ColorCode, _lastInferredColor, v => ColorCode = v, v => _lastInferredColor = v);
        TryApplyCode(result.TextureCode, TextureCode, _lastInferredTexture, v => TextureCode = v, v => _lastInferredTexture = v);
        TryApplyCode(result.MineralCode, MineralCode, _lastInferredMineral, v => MineralCode = v, v => _lastInferredMineral = v);
        TryApplyGrain(result.GrainSize);
    }

    void TryApplyCode(int? inferred, int? current, int? lastInferred, System.Action<int?> assign, System.Action<int?> setLast)
    {
        if (!inferred.HasValue) return;
        bool canApply = !current.HasValue || current == lastInferred;
        if (!canApply) return;
        if (current != inferred) assign(inferred);
        setLast(inferred);
    }

    void TryApplyGrain(string? inferred)
    {
        if (string.IsNullOrWhiteSpace(inferred)) return;
        var current = GrainSize?.Trim();
        var last = _lastInferredGrain?.Trim();
        bool canApply = string.IsNullOrWhiteSpace(current)
                        || string.Equals(current, last, System.StringComparison.OrdinalIgnoreCase);
        if (!canApply) return;
        if (!string.Equals(current, inferred, System.StringComparison.OrdinalIgnoreCase))
            GrainSize = inferred;
        _lastInferredGrain = inferred;
    }

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
            return l?.Name ?? "";
        }
    }
    public string? LithoPattern => RefCache.Instance.Litho4(LithoCode)?.PatternKey;

    public string ColorDisplay
    {
        get
        {
            var c = RefCache.Instance.Color4(ColorCode);
            return c?.Name ?? "";
        }
    }
    public string ColorHex => RefCache.Instance.Color4(ColorCode)?.HexColor ?? "#00000000";

    public string TextureDisplay => RefCache.Instance.Texture4(TextureCode)?.Name ?? "";
    public string MineralDisplay => RefCache.Instance.Mineral4(MineralCode)?.Name ?? "";
    public string FloraFaunaDisplay => RefCache.Instance.FloraFauna4(FloraFaunaCode)?.Name ?? "";
    public string IronHydroxideDisplay => RefCache.Instance.IronHydroxide4(IronHydroxideCode)?.Name ?? "";
    public string ClasticMaterialDisplay =>
        string.Join(", ", ClasticMaterialItems.Where(x => x.IsChecked).Select(x => x.Name));
}

/// <summary>Mineral tarkibi uchun checkbox elementi.</summary>
public partial class CheckableClasticItem : ObservableObject
{
    public int Code { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] bool _isChecked;
}