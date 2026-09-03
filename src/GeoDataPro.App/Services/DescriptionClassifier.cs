using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GeoDataPro.App.Data;

namespace GeoDataPro.App.Services;

public sealed class DescriptionClassificationResult
{
    public int? LithoCode { get; init; }
    public int? ColorCode { get; init; }
    public int? TextureCode { get; init; }
    public int? MineralCode { get; init; }
    public string? GrainSize { get; init; }
}

/// <summary>
/// Description matnini semantik yaqinlik asosida tasniflaydi.
/// Qoida + sinonim + spravochnik nomlari bo'yicha scoring ishlatadi,
/// noaniq holatda qiymat qaytarmaydi.
/// </summary>
public static class DescriptionClassifier
{
    public static DescriptionClassificationResult Classify(string? description, RefCache refs)
    {
        var normalized = NormalizeText(description);
        if (string.IsNullOrWhiteSpace(normalized))
            return new DescriptionClassificationResult();

        var text = $" {normalized} ";
        var tokens = Tokenize(normalized);

        return new DescriptionClassificationResult
        {
            LithoCode = PickBestCode(
                refs.Litho.Select(x => BuildCandidate(x.Code, x.Name, x.NameRu)),
                text, tokens),
            ColorCode = PickBestCode(
                refs.Colors.Select(x => BuildColorCandidate(x)),
                text, tokens),
            TextureCode = PickBestCode(
                refs.Textures.Select(x => BuildTextureCandidate(x)),
                text, tokens),
            MineralCode = PickBestCode(
                refs.Minerals.Select(x => BuildCandidate(x.Code, x.Name, x.NameRu)),
                text, tokens),
            GrainSize = InferGrainSize(text),
        };
    }

    sealed class Candidate
    {
        public int Code { get; init; }
        public HashSet<string> Phrases { get; } = new(StringComparer.Ordinal);
    }

    static Candidate BuildCandidate(int code, params string?[] names)
    {
        var c = new Candidate { Code = code };
        foreach (var n in names)
            AddPhraseVariants(c.Phrases, n);
        return c;
    }

    static Candidate BuildColorCandidate(ColorCode color)
    {
        var c = BuildCandidate(color.Code, color.Name, color.NameRu);
        var all = NormalizeText($"{color.Name} {color.NameRu}");
        if (all.Contains("kulrang", StringComparison.Ordinal) || all.Contains("сер", StringComparison.Ordinal))
            c.Phrases.Add("kulrang");
        if (all.Contains("sariq", StringComparison.Ordinal) || all.Contains("желт", StringComparison.Ordinal))
            c.Phrases.Add("sariq");
        if (all.Contains("qizil", StringComparison.Ordinal) || all.Contains("красн", StringComparison.Ordinal))
            c.Phrases.Add("qizil");
        if (all.Contains("jigarrang", StringComparison.Ordinal) || all.Contains("корич", StringComparison.Ordinal))
            c.Phrases.Add("jigarrang");
        if (all.Contains("binafsha", StringComparison.Ordinal) || all.Contains("фиолет", StringComparison.Ordinal))
            c.Phrases.Add("binafsha");
        if (all.Contains("pushti", StringComparison.Ordinal) || all.Contains("розов", StringComparison.Ordinal))
            c.Phrases.Add("pushti");
        return c;
    }

    static Candidate BuildTextureCandidate(TextureCode texture)
    {
        var c = BuildCandidate(texture.Code, texture.Name, texture.NameRu);
        var all = NormalizeText($"{texture.Name} {texture.NameRu}");
        if (all.Contains("massiv", StringComparison.Ordinal) || all.Contains("katta hajmli", StringComparison.Ordinal))
        {
            c.Phrases.Add("massiv");
            c.Phrases.Add("massivli");
            c.Phrases.Add("беспорядочн");
        }
        if (all.Contains("bolak", StringComparison.Ordinal) || all.Contains("обломоч", StringComparison.Ordinal))
        {
            c.Phrases.Add("bo'lakli");
            c.Phrases.Add("bo'lak");
            c.Phrases.Add("комковат");
            c.Phrases.Add("обломоч");
        }
        if (all.Contains("tolqin", StringComparison.Ordinal) || all.Contains("волнист", StringComparison.Ordinal))
            c.Phrases.Add("tolqinsimon");
        if (all.Contains("gorizontal", StringComparison.Ordinal) || all.Contains("горизонтал", StringComparison.Ordinal))
            c.Phrases.Add("gorizontal");
        return c;
    }

    static int? PickBestCode(IEnumerable<Candidate> candidates, string text, HashSet<string> tokens)
    {
        int bestCode = 0;
        int bestScore = 0;
        int second = 0;

        foreach (var c in candidates)
        {
            int score = ScoreCandidate(c, text, tokens);
            if (score > bestScore)
            {
                second = bestScore;
                bestScore = score;
                bestCode = c.Code;
            }
            else if (score > second)
            {
                second = score;
            }
        }

        if (bestScore >= 90) return bestCode;                 // juda aniq ibora mosligi
        if (bestScore < 35) return null;                      // ishonch past
        if (bestScore - second < 8) return null;              // noaniq (ambiguous)
        return bestCode;
    }

    static int ScoreCandidate(Candidate c, string text, HashSet<string> tokens)
    {
        int best = 0;
        foreach (var p in c.Phrases)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var phrase = NormalizeText(p);
            if (phrase.Length == 0) continue;

            if (ContainsWholePhrase(text, phrase))
            {
                var phraseTokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                best = Math.Max(best, 90 + phraseTokens.Length * 8);
                continue;
            }

            var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(t => t.Length >= 3 || t.All(char.IsDigit))
                              .ToArray();
            if (parts.Length == 0) continue;

            if (parts.Length == 1)
            {
                var t = parts[0];
                if (tokens.Contains(t)) best = Math.Max(best, 35);
                else if (t.Length >= 5 && tokens.Any(x => x.StartsWith(t, StringComparison.Ordinal) || t.StartsWith(x, StringComparison.Ordinal)))
                    best = Math.Max(best, 24);
                continue;
            }

            int matched = parts.Count(tokens.Contains);
            double ratio = matched / (double)parts.Length;
            if (matched == parts.Length)
                best = Math.Max(best, 62 + parts.Length * 4);
            else if (matched >= 2 && ratio >= 0.66)
                best = Math.Max(best, 22 + matched * 5);
        }
        return best;
    }

    static string? InferGrainSize(string text)
    {
        if (ContainsAny(text, " mayda donador", " mayda donali", " мелкозернист", " мелкозерн", " fine grained"))
            return "mayda";
        if (ContainsAny(text, " orta donador", " o'rta donador", " orta donali", " o'rta donali", " среднезернист", " среднезерн", " medium grained"))
            return "o'rta";
        if (ContainsAny(text, " yirik donador", " yirik donali", " крупнозернист", " крупнозерн", " coarse grained"))
            return "yirik";

        // qisqa semantik belgilar (faqat yakka holda kelganda)
        if (ContainsAny(text, " mayda ", " мелкий ")) return "mayda";
        if (ContainsAny(text, " o'rta ", " orta ", " средний ")) return "o'rta";
        if (ContainsAny(text, " yirik ", " крупный ")) return "yirik";
        return null;
    }

    static bool ContainsAny(string text, params string[] phrases)
        => phrases.Any(p => text.Contains($" {NormalizeText(p)} ", StringComparison.Ordinal));

    static bool ContainsWholePhrase(string text, string phrase)
        => text.Contains($" {phrase} ", StringComparison.Ordinal);

    static void AddPhraseVariants(HashSet<string> bucket, string? raw)
    {
        var s = NormalizeText(raw);
        if (string.IsNullOrWhiteSpace(s)) return;
        bucket.Add(s);
        bucket.Add(s.Replace("  ", " "));

        // Qavs ichidagi izohlarni tashlab sodda variant ham qo'shamiz.
        var sb = new StringBuilder();
        int depth = 0;
        foreach (var ch in s)
        {
            if (ch == '(') { depth++; continue; }
            if (ch == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0) sb.Append(ch);
        }
        var noParen = NormalizeText(sb.ToString());
        if (!string.IsNullOrWhiteSpace(noParen))
            bucket.Add(noParen);
    }

    static HashSet<string> Tokenize(string normalized)
        => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .ToHashSet(StringComparer.Ordinal);

    static string NormalizeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.ToLowerInvariant();
        s = s.Replace('’', '\'').Replace('`', '\'').Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('‘', '\'');
        s = s.Replace('ў', 'у').Replace('қ', 'к').Replace('ғ', 'г').Replace('ҳ', 'х');

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '\'' || ch == '-' || ch == '/' || ch == ',')
                sb.Append(' ');
            else
                sb.Append(' ');
        }

        var compact = sb.ToString().Normalize(NormalizationForm.FormC);
        while (compact.Contains("  ", StringComparison.Ordinal))
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        return compact.Trim();
    }
}
