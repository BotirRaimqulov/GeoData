using System.Collections.Generic;
using System.Linq;

namespace GeoDataPro.App.Data;

/// <summary>Spravochniklarni (malumotlar/ jildidagi Excel + PNG asosida) to'ldiradi.</summary>
public static class Seed
{
    public static void Run(AppDbContext db)
    {
        if (!db.LithoCodes.Any())
        {
            db.LithoCodes.AddRange(LithoSeed());
            db.SaveChanges();
        }
        if (!db.ColorCodes.Any())
        {
            db.ColorCodes.AddRange(ColorSeed());
            db.SaveChanges();
        }
        if (!db.TextureCodes.Any())
        {
            db.TextureCodes.AddRange(TextureSeed());
            db.SaveChanges();
        }
        if (!db.MineralCodes.Any())
        {
            db.MineralCodes.AddRange(MineralSeed());
            db.SaveChanges();
        }

        BackfillRussianNames(db);
        if (!db.DescriptionTemplates.Any())
        {
            foreach (var t in new[]
            {
                "Qumtosh kulrang tusda", "Qum qumtoshli kulrang tusli",
                "Qum yashilsimon kulrang tusda", "Qum kulrang tusli",
                "Qum och sariq tusli", "Alevrolit jigarrang tusli",
                "Qum alevrolit kulrang sariq dog'li", "Gil kulrang tusli",
            })
                db.DescriptionTemplates.Add(new DescriptionTemplate { Text = t });
            db.SaveChanges();
        }
    }

    // ---- Litologik kodlar: "Litho geofizika bo'yicha.xlsx" (kengroq roʻyxat) ----
    // PatternKey -> Assets/litho/*.png ; HexColor -> patterni boʻlmaganda placeholder
    // (code, o'zbekcha, ruscha, png, hex)
    static readonly (int code, string uz, string ru, string? png, string hex)[] LithoRows =
    {
        (4,  "Qum",                              "Песок",                                     "qum.png",                          "#F2E2A8"),
        (5,  "Qumtosh",                           "Песчаник",                                  "qumtosh.png",                      "#E8D48A"),
        (6,  "Alevrolit",                         "Алевролит",                                 "alevrolit.png",                    "#D9CBA0"),
        (7,  "Gil (Glina)",                       "Глина",                                     "glina.png",                        "#C9C0B0"),
        (8,  "Qum qumtoshli",                     "Песок с прослоями песчаника",               "qum_qumtoshli.png",                "#EDDC97"),
        (9,  "Mergel",                            "Мергель",                                   "mergel.png",                       "#BFC9B8"),
        (10, "Qum gilli",                         "Песок глинистый",                           "qum_gilli.png",                    "#E4D6A6"),
        (11, "Qum alevrolitli",                   "Песок алевролитистый",                      "qum_alevrolitli.png",              "#E2D6AB"),
        (12, "Qum, glina bilan qatlamlashgan",    "Песок с прослоями глины",                   "qum_glina_bilan_qatlamlashgan.png","#DED2A8"),
        (13, "Alevrolit qumli",                   "Алевролит песчанистый",                     "alevrolit_qumli.png",              "#DBCEA2"),
        (14, "Alevrolit gilli",                   "Алевролит глинистый",                       "alevrolit_gilli.png",              "#CFC4A6"),
        (15, "Gravelit",                          "Гравелит",                                  "gravelit.png",                     "#D8C69A"),
        (16, "Angidrit",                          "Ангидрит",                                  "angidrit.png",                     "#CBD5E0"),
        (17, "Gips",                              "Гипс",                                      "gips.png",                         "#E6E2EC"),
        (18, "Alevrolit qumloq gilli",           "Алевролит песчано-глинистый",                "alevrolit_qumloq_gilli.png",       "#CEC5A4"),
        (19, "Gil, qum bilan qatlamlashgan",      "Глина с прослоями песка",                   "glina_qum_bilan_qatlamlashgan.png","#CDC4AE"),
        (20, "Gil qumloq alevrolitli",           "Глина песчано-алевролитистая",               "glina_qum_alivroletli.png",        "#C8C1AA"),
        (21, "Gil ko'mirli",                      "Глина углистая",                            "glina_komirli.png",                "#8C8878"),
        (22, "Gil qumtoshli",                     "Глина песчанистая (карбонатная)",           "glina_qumtoshli.png",              "#CBC2AA"),
        (23, "Gil shag'altoshli",                 "Глина с галькой, галечник",                 "glina_shagaltoshli.png",           "#C3BBA6"),
        (24, "Qum shag'altoshli",                 "Песок с галькой, галечник",                 "qum_shagaltoshli.png",             "#E0D2A0"),
        (25, "Gil qumli",                         "Глина песчанистая",                         "glina_qumli.png",                  "#CCC3AC"),
        (26, "Cho'kindi brekchi",                 "Осадочная брекчия",                         "chokindi_brekcha.png",             "#D2B48C"),
        (27, "Ohaktosh",                          "Известняк",                                 "ohaktosh.png",                     "#DDE6EA"),
        (28, "Qumtosh karbonatli",               "Песчаник на карбонатном цементе",            "qumtosh_karbonatli.png",           "#E4DCB0"),
        (29, "Suglinok va supes",                "Суглинок и супесь",                          "suglinok_va_supes.png",            "#D6C7A6"),
        (30, "Qumtosh bobovnik",                 "Песчаник бобовниковый",                     "qumtosh_bobovnik.png",             "#E6D69A"),
        (31, "Qumli ohaktosh",                   "Известняк песчанистый",                     "qumli_ohaktosh.png",               "#E0E2C4"),
        (32, "Granit",                            "Гранит",                                    "granit.png",                       "#E8B7B0"),
        (33, "Dolomit",                           "Доломит",                                   "dolomit.png",                      "#D8E4E8"),
        (34, "Slanest",                           "Сланец",                                    "slanest.png",                      "#B8BCC0"),
    };

    static IEnumerable<LithoCode> LithoSeed() => LithoRows.Select(r => new LithoCode
    {
        Code = r.code, Name = r.uz, NameRu = r.ru, PatternKey = r.png, HexColor = r.hex
    });

    // ---- Kern rangi: "По Керну" varaqasi + Assets/kern/*.png ----
    // (code, o'zbekcha, ruscha, hex)
    static readonly (int code, string uz, string ru, string hex)[] ColorRows =
    {
        (1,  "Kulrang",                                 "Серый",                                       "#A9ACB0"),
        (2,  "Och kulrang",                             "Светло-серый",                                "#C7CACE"),
        (3,  "To'q kulrang",                            "Тёмно-серый",                                 "#6E7175"),
        (4,  "Kulrangda sariq dog'li",                  "Серый с жёлтыми пятнами",                     "#B7B48C"),
        (5,  "Kulrang yashilsimon rangda",              "Серый с зеленоватым оттенком",                "#9BAE9A"),
        (6,  "Kulrang jigarrang dog'li",                "Серый с коричневыми пятнами",                 "#A2988C"),
        (7,  "Kulrangda olovrang dog'li",              "Серый с огненно-красными пятнами",             "#B49A8C"),
        (8,  "Sariq",                                   "Жёлтый",                                      "#E6C84E"),
        (9,  "To'q sariq",                              "Тёмно-жёлтый",                                "#C9A83A"),
        (10, "Och sariq",                               "Светло-жёлтый",                               "#F0DE9A"),
        (11, "Sariq yashilsimon",                       "Жёлтый с зеленоватым оттенком",               "#CBD08A"),
        (12, "Sariq olovrang dog'li",                   "Жёлтый с огненно-красными пятнами",           "#DAA95A"),
        (13, "Sariq qizil rang dog'li",                 "Жёлтый с красными пятнами",                   "#D98A5A"),
        (14, "Qizil",                                   "Красный",                                     "#C0504D"),
        (15, "Och qizil",                               "Светло-красный",                              "#D98A88"),
        (16, "Qizil sariq rang dog'li",                 "Красный с жёлтыми пятнами",                   "#D07A55"),
        (17, "Qizil kulrang dog'li",                    "Красный с серыми пятнами",                    "#B07A78"),
        (18, "Jigarrang",                               "Коричневый",                                  "#8B5A3C"),
        (19, "Jigarrang binafsha rang dog'li",          "Коричневый с фиолетовыми пятнами",            "#7E5A6A"),
        (20, "Jigarrang sariq dog'li",                  "Коричневый с жёлтыми пятнами",                "#9A7B4C"),
        (21, "Jigarrang kulrang dog'li",                "Коричневый с серыми пятнами",                 "#8A7A6C"),
        (22, "Binafsharang",                            "Фиолетовый",                                  "#8E6FA8"),
        (23, "Och binafsha",                            "Светло-фиолетовый",                           "#B49AC8"),
        (24, "Pushti rang",                             "Розовый",                                     "#E0A8B4"),
        (25, "Och pushti",                              "Светло-розовый",                              "#F0CAD4"),
        (26, "Sariq kirsimon rangda",                   "Грязно-жёлтый",                               "#B8A87A"),
        (27, "Qizil binafsha rang dog'li",              "Красный с фиолетовыми пятнами",               "#A05A78"),
        (28, "To'q jigarrang",                          "Тёмно-коричневый",                            "#5E3B28"),
    };

    static IEnumerable<ColorCode> ColorSeed() => ColorRows.Select(r => new ColorCode
    {
        Code = r.code, Name = r.uz, NameRu = r.ru, HexColor = r.hex
    });

    // ---- Tekstura: "Текстура/" papkasi ----
    // (o'zbekcha, ruscha, png)
    static readonly (string uz, string ru, string png)[] TextureRows =
    {
        ("To'g'ri chiziqli",                       "Прямолинейная (ровная)",                        "togri_chiziqli.png"),
        ("Gorizontal uzluksiz",                    "Горизонтальная непрерывная слоистость",         "gorizontal_uzluksiz.png"),
        ("Gorizontal uzlukli",                     "Горизонтальная прерывистая слоистость",         "gorizontal_uzlukli.png"),
        ("To'lqinsimon iz",                        "Волнистая слоистость",                          "tolqinsimon_iz.png"),
        ("Qiyshiq qirrali",                        "Косая слоистость",                              "qiyshiq_qirrali.png"),
        ("Linza ko'rinishli",                      "Линзовидная слоистость",                        "linza_korinishli.png"),
        ("Mulda shaklli",                          "Мульдообразная слоистость",                     "mulda_shaklli.png"),
        ("Bo'lakli",                               "Комковатая (обломочная)",                       "bolakli.png"),
        ("Noaniq qatlamlashgan",                   "Неяснослоистая",                                "noaniq_qatlamlashgan.png"),
        ("Katta hajmli",                           "Массивная (беспорядочная)",                     "katta_hajmli.png"),
        ("Karbonatli",                             "Карбонатная",                                   "karbonatli.png"),
        ("Chig'anoq",                              "Раковистая",                                    "chiganoq.png"),
        ("Chuvalchang izi",                        "Следы червей (ходы илоедов)",                   "chuvalchang_izi.png"),
        ("O‘simlik barglarining izlari",           "Отпечатки листьев растений",                    "osimlik_barglarining_izlari.png"),
        ("O‘simlik tomirlarining izlari",          "Следы корней растений",                         "osimlik_tomirlarining_izlari.png"),
        ("Malyuskalar va chig‘anoqlarining izlari","Отпечатки моллюсков и раковин",                 "malyuskalar_va_chiganoqlarining_izlari.png"),
        ("Baliq suyagining fosfat qoldiqlari",     "Фосфатные остатки рыбьих костей",               "baliq_suyagining_fosfat_qoldiqlari.png"),
        ("O'xshash minerallar",                    "Стяжения минералов",                            "oxshash_minerallar.png"),
    };

    static IEnumerable<TextureCode> TextureSeed()
    {
        int c = 1;
        return TextureRows.Select(r => new TextureCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    // ---- Mineralizatsiya: "Минерализация/" papkasi ----
    // (o'zbekcha, ruscha, png)
    static readonly (string uz, string ru, string png)[] MineralRows =
    {
        ("Glaukonit",                                        "Глауконит",                                    "glaukonit.png"),
        ("Kalsit",                                           "Кальцит",                                      "kalsit.png"),
        ("Dolomit",                                          "Доломит",                                      "dolomit.png"),
        ("Kaolinitli",                                        "Каолинит",                                      "kaolinitli.png"),
        ("Jelvakli fosforit",                                "Желваковый фосфорит",                           "jelvakli_fosforit.png"),
        ("Konkretsion",                                       "Конкреционный",                                 "konkretsion.png"),
        ("Selestin",                                          "Целестин",                                      "selestin.png"),
        ("Markazit",                                          "Марказит",                                      "markazit.png"),
        ("Molibdenit",                                        "Молибденит",                                    "molibdenit.png"),
        ("Shaffof kristalli",                                "Прозрачные кристаллы",                          "shaffof_kristalli.png"),
        ("Loyqa tuproqli",                                    "Ожелезнённый (глинистый)",                      "loyqa_tuproqli.png"),
        ("Yupqa dispersli (sochiluvchan)",                  "Тонкодисперсный (рассеянный)",                  "yupqa_dispersli_sochiluvchan.png"),
        ("Yupqa donali, massiv",                            "Тонкозернистый, массивный (в т.ч. ангидрит)",   "yupqa_donali_massiv_jumladan_angidrit.png"),
        ("Ko'mir qoldiqlari (detrit)",                       "Углистый детрит",                               "komir_qoldiqlari_detrit.png"),
        ("O‘simlik ildizlari",                               "Корни растений",                                "osimlik_ildizlari.png"),
        ("Yirik uglerodli yog‘och parchalari",              "Крупные обугленные обломки древесины",          "yirik_uglerodli_yogoch_parchalari.png"),
        ("Yog‘ochning kremniylashgan bo‘lakchalari",        "Окремнелые обломки древесины",                  "yogochning_kremniylashgan_bolakchalari.png"),
        ("Akula tishi",                                       "Зуб акулы",                                     "akula_tishi.png"),
        ("Yelkaoyoqlilar",                                    "Брахиоподы",                                    "yelkaoyoqlilar.png"),
        ("Ko'p tarqalgan malyuskalar turi",                  "Распространённые виды моллюсков",               "kop_tarqalgan_malyuskalar_turi.png"),
        ("Quruqlikdagi umurtqali hayvonlarning suyaklari",  "Кости наземных позвоночных",                    "quruqlikdagi_umurtqali_hayvonlarning_suyaklari.png"),
    };

    static IEnumerable<MineralCode> MineralSeed()
    {
        int c = 1;
        return MineralRows.Select(r => new MineralCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    /// <summary>Eski bazalarda NameRu bo'sh qatorlarni ruscha nom bilan to'ldiradi (kod bo'yicha).</summary>
    static void BackfillRussianNames(AppDbContext db)
    {
        bool changed = false;

        var lithoRu = LithoRows.ToDictionary(r => r.code, r => r.ru);
        foreach (var x in db.LithoCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (lithoRu.TryGetValue(x.Code, out var ru)) { x.NameRu = ru; changed = true; }

        var colorRu = ColorRows.ToDictionary(r => r.code, r => r.ru);
        foreach (var x in db.ColorCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (colorRu.TryGetValue(x.Code, out var ru)) { x.NameRu = ru; changed = true; }

        var textureRu = TextureRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.TextureCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (textureRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        var mineralRu = MineralRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.MineralCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (mineralRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        if (changed) db.SaveChanges();
    }
}
