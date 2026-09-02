# GeoData Pro

Geologik dala ma'lumotlarini kiritish va boshqarish uchun Windows desktop ilovasi
(WPF, .NET 9, SQLite). Rasmda ko'rsatilgan "Dala jurnali" interfeysiga mos.

## Ishga tushirish

```bash
dotnet run --project src/GeoDataPro.App
```

Yoki `GeoDataPro.sln` ni Visual Studio / Rider da oching.

## Talablar
- .NET 9 SDK (Windows)

## Ma'lumotlar bazasi
SQLite fayl: `%LOCALAPPDATA%\GeoDataPro\geodata.db`
Birinchi ishga tushirishda avtomatik yaratiladi va spravochniklar bilan to'ldiriladi
(`malumotlar/` jildidagi Excel + PNG asosida). Loyihalar va quduqlar bo'sh holda boshlanadi —
ularni "Quduqlar" bo'limidan qo'shasiz.

## Bo'limlar
| Bo'lim | Tavsif |
|--------|--------|
| Umumiy ko'rinish | Statistika, litologik tarkib diagrammasi |
| **Dala jurnali** | Asosiy jadval: TOP/BOTTOM, interval, kern chiqishi, litol. kod (belgi bilan), rang, tekstura, tavsif. Detali paneli, Svodka, quduq profili |
| Namuna (Образцы) | Namuna intervallari |
| SRP (Kern GK) | Kern bo'yicha gamma-karotaj (Core_GK) + karotaj chizig'i |
| Litologik kodlar / Kern ranglari / Teksturalar / Mineralizatsiya / Zonalar / Tavsif shablonlari | Spravochnik tahrirlagichlari |
| Quduqlar / Loyihalar | Loyiha va quduq CRUD |
| Ma'lumotlar tekshiruvi | Intervallar kesishishi, kern chiqishi > interval, noma'lum kodlar va h.k. |
| Import / Eksport | `Шаблон.xlsx` formatida Excel import/eksport (Dala jurnali + Namuna + SRP) |
| Zaxira nusxa | `.db` bazani saqlash / tiklash |

## Hotkeys (Dala jurnali)
- `Ctrl+S` — saqlash
- `Ctrl+N` — yangi qator
- `Ctrl+D` — qatorni nusxalash
- `Del` — qatorni o'chirish

## Struktura
```
src/GeoDataPro.App/
├── Data/          # EF Core ent(Entities, AppDbContext, Seed)
├── Services/      # AppState, RefCache, ExcelService (ClosedXML)
├── ViewModels/    # MVVM (CommunityToolkit.Mvvm)
├── Views/         # UserControl lar (har bo'lim uchun bittadan)
├── Theme/         # Colors / Controls / Converters ResourceDictionary
└── Assets/        # litho / texture / mineral / kern PNG belgilari
```
