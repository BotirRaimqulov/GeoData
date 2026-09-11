using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoDataPro.Core.Data;

/// <summary>Spravochniklarni (malumotlar/ jildidagi Excel + PNG asosida) to'ldiradi.</summary>
public static class Seed
{
    public static void Run(AppDbContext db)
    {
        if (!db.LithoCodes.Any()) { db.LithoCodes.AddRange(LithoSeed()); db.SaveChanges(); }
        if (!db.ColorCodes.Any()) { db.ColorCodes.AddRange(ColorSeed()); db.SaveChanges(); }
        if (!db.TextureCodes.Any()) { db.TextureCodes.AddRange(TextureSeed()); db.SaveChanges(); }
        if (!db.MineralCodes.Any()) { db.MineralCodes.AddRange(MineralSeed()); db.SaveChanges(); }
        if (!db.FloraFaunaCodes.Any()) { db.FloraFaunaCodes.AddRange(FloraFaunaSeed()); db.SaveChanges(); }
        if (!db.IronHydroxideCodes.Any()) { db.IronHydroxideCodes.AddRange(IronHydroxideSeed()); db.SaveChanges(); }
        if (!db.ClasticMaterialCodes.Any()) { db.ClasticMaterialCodes.AddRange(ClasticMaterialSeed()); db.SaveChanges(); }

        MigrateOrganicRemainsFromMinerals(db);
        ReseedMineralsIfOutdated(db);
        ReseedTexturesIfOutdated(db);
        ReseedFloraFaunaIfOutdated(db);
        ReseedIronHydroxidesIfOutdated(db);
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

    // ---- Litologik kodlar ----
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
        (35, "Mergel gilli",                      "Мергель глинистый",                         "mergel_gilli.png",                 "#B8C2B0"),
        (36, "Qumtosh gilli",                     "Песчаник глинистый",                        "qumtosh_gilli.png",                "#DACED0"),
        (37, "Qumtosh ohaktoshli",                "Песчаник известняковый",                    "qumtosh_ohaktoshli.png",           "#E0DCA8"),
    };

    static IEnumerable<LithoCode> LithoSeed() => LithoRows.Select(r => new LithoCode
    {
        Code = r.code, Name = r.uz, NameRu = r.ru, PatternKey = r.png, HexColor = r.hex
    });

    // ---- Kern rangi ----
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

    // ---- Tekstura (sedimentar tuzilish turlari) ----
    static readonly (string uz, string ru, string? png)[] TexturesRows =
    {
        // --- Qatlamsiz (Неслоистая) ---
        ("Qatlamsiz: Massiv",                            "Неслоистая: массивная",                    "qatlamsiz_massiv.png"),
        ("Qatlamsiz: Kesaksimon",                        "Неслоистая: комковая",                     "qatlamsiz_kesaksimon.png"),
        ("Qatlamsiz: Noaniq",                            "Неслоистая: неясная",                      "qatlamsiz_noaniq.png"),

        // --- Qatlamli: gorizontal (Слоистая: горизонтальная) ---
        ("Qatlamli gorizontal: Tutash",                  "Слоистая горизонтальная: сплошная",        "qatlamli_gorizontal_tutash.png"),
        ("Qatlamli gorizontal: Uzuq-yuluq",              "Слоистая горизонтальная: прерывистая",     "qatlamli_gorizontal_uzuq_yuluq.png"),

        // --- Qatlamli: to'lqinsimon (Слоистая: волнистая) ---
        ("Qatlamli to'lqinsimon: Linzasimon",            "Слоистая волнистая: линзовидная",          "qatlamli_tolqinsimon_linzasimon.png"),
        ("Qatlamli to'lqinsimon: Muldasimon",            "Слоистая волнистая: мульдообразная",       "qatlamli_tolqinsimon_muldasimon.png"),

        // --- Qatlamli: qiyshiq (Слоистая: косая) ---
        ("Qatlamli qiyshiq: To'g'ri chiziqli",           "Слоистая косая: прямолинейная",            "qatlamli_qiyshiq_togri_chiziqli.png"),
        ("Qatlamli qiyshiq: Birlashuvchi",               "Слоистая косая: сходящаяся",               "qatlamli_qiyshiq_birlashuvchi.png"),
        ("Qatlamli qiyshiq: Egri chiziqli",              "Слоистая косая: криволинейная",            "qatlamli_qiyshiq_egri_chiziqli.png"),

        // --- Boshqalar ---
        ("To'lqinsimon jimjima izlari",                  "Знаки волнистой ряби",                     "tolqinsimon_jimjima_izlari.png"),
    };

    static IEnumerable<TextureCode> TextureSeed()
    {
        int c = 1;
        return TexturesRows.Select(r => new TextureCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    static void ReseedTexturesIfOutdated(AppDbContext db)
    {
        var newNames = new HashSet<string>(TexturesRows.Select(r => r.uz), StringComparer.Ordinal);
        var existing = db.TextureCodes.ToList();
        if (existing.Any(x => newNames.Contains(x.Name))) return;
        db.TextureCodes.RemoveRange(existing);
        db.SaveChanges();
        db.TextureCodes.AddRange(TextureSeed());
        db.SaveChanges();
    }

    // ---- Mineralizatsiya (keng ro'yxat) ----
    static readonly (string uz, string ru, string? png)[] MineralsRows =
    {
        // --- Sulfidlar ---
        ("Pirit: Aniq kristallik",                        "Пирит: яснокристаллический",                       "pirit_aniq_kristallik.png"),
        ("Pirit: Konkretsion",                            "Пирит: конкреционный",                             "pirit_konkretsion.png"),
        ("Pirit: Mayda dispers (sochuvchan)",             "Пирит: тонкодисперсный (сыпучка)",                 "pirit_mayda_dispers.png"),
        ("Xalkopirit",                                    "Халькопирит",                                      "xalkopirit.png"),
        ("Markazit",                                      "Марказит",                                         "markazit.png"),
        ("Galenit",                                       "Галенит",                                          "galenit.png"),
        ("Sfalerit",                                      "Сфалерит",                                         "sfalerit.png"),
        ("Molibdenit",                                    "Молибденит",                                       "molibdenit.png"),

        // --- Karbonatlar ---
        ("Kaltsit",                                       "Кальцит",                                          "kaltsit.png"),
        ("Dolomit",                                       "Доломит",                                          "dolomit.png"),
        ("Siderit",                                       "Сидерит",                                          null),
        ("Ankerit",                                       "Анкерит",                                          null),

        // --- Kremniyli minerallar ---
        ("Kvars",                                         "Кварц",                                            null),
        ("Xalsedon",                                      "Халцедон",                                         null),
        ("Opal",                                          "Опал",                                             null),

        // --- Loy minerallari ---
        ("Kaolinit",                                      "Каолинит",                                         "kaolinit.png"),
        ("Illit",                                         "Иллит",                                            null),
        ("Montmorillonit",                                "Монтмориллонит",                                   null),
        ("Smektit",                                       "Смектит",                                          null),

        // --- Fosfatlar ---
        ("Nodulyar (toshsimon) fosforit",                 "Желваковый фосфорит",                              "zhelvakovi_fosforit.png"),
        ("Fosforit: qatlamli",                            "Фосфорит: пластовый",                              null),

        // --- Sulfatlar ---
        ("Gips: Mayda donador, massiv (shu jumladan angidrit)", "Гипс: тонкозернистый, массивный (в том числе ангидрит)", "gips_mayda_donador_massiv.png"),
        ("Gips: Kristallik",                              "Гипс: кристаллический",                            null),
        ("Angidrit",                                      "Ангидрит",                                         null),
        ("Tselestin",                                     "Целестин",                                         "tselestin.png"),
        ("Barit",                                         "Барит",                                            null),

        // --- Temir minerallari ---
        ("Gematit",                                       "Гематит",                                          null),
        ("Magnetit",                                      "Магнетит",                                         null),
        ("Gidrogematit",                                  "Гидрогематит",                                     null),
        ("Getit",                                         "Гётит",                                            null),
        ("Limonit",                                       "Лимонит",                                          null),

        // --- Marganes ---
        ("Piroluzit",                                     "Пиролюзит",                                        null),
        ("Psilomelan",                                    "Псиломелан",                                       null),

        // --- Yashil minerallar ---
        ("Glaukonit",                                     "Глауконит",                                        "glaukonit.png"),

        // --- Dala shpati va boshqa detrit minerallar ---
        ("Plagioklaz",                                    "Плагиоклаз",                                       null),
        ("Kaliyli dala shpati",                           "Калиевый полевой шпат",                            null),
        ("Muskovit",                                      "Мусковит",                                         null),
        ("Biotit",                                        "Биотит",                                           null),
        ("Xlorit",                                        "Хлорит",                                           null),

        // --- Evaporit minerallari ---
        ("Galit",                                         "Галит",                                            null),
        ("Silvin",                                        "Сильвин",                                          null),
        ("Karnallit",                                     "Карналлит",                                        null),
    };

    static IEnumerable<MineralCode> MineralSeed()
    {
        int c = 1;
        return MineralsRows.Select(r => new MineralCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    /// <summary>
    /// Mavjud bazadagi mineral yozuvlaridan yangi ro'yxatda yo'q bo'lganlarni qo'shadi.
    /// Mavjud yozuvlar o'chirilmaydi — journal qatorlaridagi kodlar saqlanib qoladi.
    /// </summary>
    static void ReseedMineralsIfOutdated(AppDbContext db)
    {
        var existingNames = new HashSet<string>(db.MineralCodes.Select(x => x.Name), StringComparer.Ordinal);
        var toAdd = MineralsRows.Where(r => !existingNames.Contains(r.uz)).ToList();
        if (toAdd.Count == 0) return;
        int nextCode = db.MineralCodes.Any() ? db.MineralCodes.Max(x => x.Code) + 1 : 1;
        foreach (var r in toAdd)
            db.MineralCodes.Add(new MineralCode { Code = nextCode++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
        db.SaveChanges();
    }

    // ---- Flora-Fauna ----
    static readonly (string uz, string ru, string? png)[] FloraFaunaRows =
    {
        ("Ko'mir qoldiqlari (detrit)",                       "Углистые остатки (детрит)",                     "komir_qoldiqlari_detrit.png"),
        ("Yirik uglerodli yog'och parchalari",               "Крупные углефицированные обломки древесины",    "yirik_uglerodli_yogoch_parchalari.png"),
        ("Qattiqlashgan yog'och bo'laklari",                 "Окремнелые обломки древесины",                  "yogochning_kremniylashgan_bolakchalari.png"),
        ("O'simlik ildizlari",                               "Корни растений",                                "osimlik_ildizlari.png"),
        ("Baliq suyaklarining fosfatli qoldiqlari",          "Фосфатные костные остатки рыб",                 "baliq_suyagining_fosfat_qoldiqlari.png"),
        ("Akula tishi",                                      "Зубы акул",                                     "akula_tishi.png"),
        ("Quruqlikdagi umurtqali hayvonlarning suyaklari",   "Кости наземных позвоночных",                    "quruqlikdagi_umurtqali_hayvonlarning_suyaklari.png"),
        ("O'simlik ildizlari izlari",                        "Отпечатки корней растений",                     "osimlik_ildizlari_izlari.png"),
        ("O'simlik barglari",                                "Отпечатки листьев растений",                    "osimlik_barglari_izlari.png"),
    };

    static IEnumerable<FloraFaunaCode> FloraFaunaSeed()
    {
        int c = 1;
        return FloraFaunaRows.Select(r => new FloraFaunaCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    /// <summary>
    /// Eski bazalarda "Gastropodlar", "Braxiopodlar" va "O'simlik barglari izlari" yozuvlari
    /// bo'lishi mumkin — ularni yangi ro'yxatga moslashtiradi.
    /// </summary>
    static void ReseedFloraFaunaIfOutdated(AppDbContext db)
    {
        // "O'simlik barglari izlari" -> "O'simlik barglari" nomini yangilaymiz
        var renamed = db.FloraFaunaCodes.FirstOrDefault(x => x.Name == "O'simlik barglari izlari");
        if (renamed != null) renamed.Name = "O'simlik barglari";

        var newNames = new HashSet<string>(FloraFaunaRows.Select(r => r.uz), StringComparer.Ordinal);
        var stale = db.FloraFaunaCodes.Where(x => !newNames.Contains(x.Name)).ToList();
        if (stale.Count == 0 && renamed == null) return;
        db.FloraFaunaCodes.RemoveRange(stale);
        db.SaveChanges();
    }

    // ---- Temir gidrooksidlari (Gidrookisleniya) ----
    static readonly (string uz, string ru, string? png)[] IronHydroxideRows =
    {
        ("Temir gidrooksidlari: Qizil",          "Гидроокислы железа: красные",         "temir_gidrooksidlari_qizil.png"),
        ("Temir gidrooksidlari: Qo'ng'ir-sariq", "Гидроокислы железа: буро-желтые",     "temir_gidrooksidlari_qongir_sariq.png"),
    };

    static IEnumerable<IronHydroxideCode> IronHydroxideSeed()
    {
        int c = 1;
        return IronHydroxideRows.Select(r => new IronHydroxideCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    /// <summary>
    /// Eski bazalarda "Marganes oksidlari", "Uran qorasi" va boshqa yozuvlar bo'lishi mumkin —
    /// yangi 2 ta yozuv bilan mos kelmaydiganlarni o'chiramiz.
    /// </summary>
    static void ReseedIronHydroxidesIfOutdated(AppDbContext db)
    {
        var newNames = new HashSet<string>(IronHydroxideRows.Select(r => r.uz), StringComparer.Ordinal);
        var stale = db.IronHydroxideCodes.Where(x => !newNames.Contains(x.Name)).ToList();
        if (stale.Count == 0) return;
        db.IronHydroxideCodes.RemoveRange(stale);
        db.SaveChanges();
    }

    // ---- Mineral tarkibi (oblomochny material) ----
    static readonly (string uz, string ru, string? png)[] ClasticMaterialRows =
    {
        ("Kvars (Q)",             "Кварц (Q)",              null),
        ("Dala shpatlari (Fs)",   "Полевые шпаты (Fs)",     null),
        ("Muskovit (Mu)",         "Мусковит (Mu)",           null),
        ("Biotit (Bi)",           "Биотит (Bi)",             null),
    };

    static IEnumerable<ClasticMaterialCode> ClasticMaterialSeed()
    {
        int c = 1;
        return ClasticMaterialRows.Select(r => new ClasticMaterialCode { Code = c++, Name = r.uz, NameRu = r.ru, PatternKey = r.png });
    }

    // ---- Migratsiya metodlari ----

    static readonly string[] FloraFaunaMigratedFromMineral =
    {
        "Ko'mir qoldiqlari (detrit)",
        "O'simlik ildizlari",
        "Yirik uglerodli yog'och parchalari",
        "Yog'ochning kremniylashgan bo'lakchalari",
        "Qattiqlashgan yog'och bo'laklari",
        "Akula tishi",
        "Yelkaoyoqlilar",
        "Yelkaoyoqlilar (braxiopodalar)",
        "Ko'p tarqalgan malyuskalar turi",
        "Quruqlikdagi umurtqali hayvonlarning suyaklari",
        "Gastropodalar",
        "Gastropodlar",
        "Braxiopodlar",
    };

    static void MigrateOrganicRemainsFromMinerals(AppDbContext db)
    {
        var movedNames = new HashSet<string>(FloraFaunaMigratedFromMineral, StringComparer.Ordinal);
        var stale = db.MineralCodes.Where(m => movedNames.Contains(m.Name)).ToList();
        if (stale.Count == 0) return;

        var floraByName = db.FloraFaunaCodes.ToDictionary(f => f.Name, StringComparer.Ordinal);
        bool changed = false;
        foreach (var old in stale)
        {
            if (!floraByName.TryGetValue(old.Name, out var flora)) continue;
            foreach (var row in db.JournalRows.Where(r => r.MineralCode == old.Code))
            {
                row.FloraFaunaCode = flora.Code;
                row.MineralCode = null;
                changed = true;
            }
            db.MineralCodes.Remove(old);
            changed = true;
        }
        if (changed) db.SaveChanges();
    }

    static void BackfillRussianNames(AppDbContext db)
    {
        bool changed = false;

        var lithoRu = LithoRows.ToDictionary(r => r.code, r => r.ru);
        foreach (var x in db.LithoCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (lithoRu.TryGetValue(x.Code, out var ru)) { x.NameRu = ru; changed = true; }

        var colorRu = ColorRows.ToDictionary(r => r.code, r => r.ru);
        foreach (var x in db.ColorCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (colorRu.TryGetValue(x.Code, out var ru)) { x.NameRu = ru; changed = true; }

        var textureRu = TexturesRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.TextureCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (textureRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        var mineralRu = MineralsRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.MineralCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (mineralRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        var floraFaunaRu = FloraFaunaRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.FloraFaunaCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (floraFaunaRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        var ironRu = IronHydroxideRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.IronHydroxideCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (ironRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        var clasticRu = ClasticMaterialRows.ToDictionary(r => r.uz, r => r.ru);
        foreach (var x in db.ClasticMaterialCodes.Where(x => x.NameRu == null || x.NameRu == ""))
            if (clasticRu.TryGetValue(x.Name, out var ru)) { x.NameRu = ru; changed = true; }

        if (changed) db.SaveChanges();
    }
}
