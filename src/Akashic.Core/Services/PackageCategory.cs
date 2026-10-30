using System.Text;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services;

public static partial class PackageCategory
{
    public const string Other = "其他";

    public static readonly string[] All =
    [
        "服饰", "模型", "场景", "道具", "纹理", "动画", "音频", "插画", "文档", "工具", Other
    ];

    private static readonly (string Label, string Category)[] BoothLabels =
    [
        ("3D衣装", "服饰"),
        ("衣装", "服饰"),
        ("アクセサリー", "服饰"),
        ("3Dモデル", "模型"),
        ("3D環境", "场景"),
        ("ワールド", "场景"),
        ("3D小道具", "道具"),
        ("テクスチャ", "纹理"),
        ("アニメーション", "动画"),
        ("音楽", "音频"),
        ("イラスト", "插画"),
        ("ドキュメント", "文档"),
        ("ツール", "工具"),
        ("ソフトウェア", "工具"),
    ];

    private static readonly (string Word, string Category)[] Keywords =
    [
        ("衣装", "服饰"), ("衣服", "服饰"), ("服装", "服饰"), ("スカート", "服饰"),
        ("セーラー", "服饰"), ("アクセサリー", "服饰"), ("髪型", "服饰"), ("ヘア", "服饰"),
        ("dress", "服饰"), ("outfit", "服饰"), ("cloth", "服饰"), ("wear", "服饰"),
        ("shoes", "服饰"), ("hair", "服饰"), ("accessor", "服饰"),

        ("モデル", "模型"), ("model", "模型"), ("アバター", "模型"), ("avatar", "模型"),
        ("body", "模型"), ("head", "模型"), ("ボディ", "模型"), ("morph", "模型"),
        ("シェイプキー", "模型"), ("shapekey", "模型"),

        ("小道具", "道具"), ("prop", "道具"), ("ギミック", "道具"), ("gimmick", "道具"),
        ("system", "道具"), ("ツール", "道具"), ("tool", "道具"), ("utility", "道具"),
        ("shader", "道具"), ("シェーダー", "道具"), ("システム", "道具"),

        ("ソフト", "工具"), ("software", "工具"), ("plugin", "工具"), ("エディタ", "工具"),
        ("converter", "工具"), ("コンバータ", "工具"), ("toon", "工具"),

        ("ワールド", "场景"), ("world", "场景"), ("環境", "场景"), ("背景", "场景"),
        ("skybox", "场景"), ("room", "场景"), ("部屋", "场景"), ("interior", "场景"),
        ("家具", "场景"), ("furniture", "场景"), ("街", "场景"), ("建物", "场景"),

        ("テクスチャ", "纹理"), ("texture", "纹理"), ("マテリアル", "纹理"), ("material", "纹理"),

        ("アニメーション", "动画"), ("animation", "动画"), ("モーション", "动画"),
        ("motion", "动画"), ("ダンス", "动画"), ("dance", "动画"),

        ("音楽", "音频"), ("bgm", "音频"), ("sound", "音频"), ("効果音", "音频"),
        ("ボイス", "音频"), ("voice", "音频"), ("sfx", "音频"),

        ("イラスト", "插画"), ("illustration", "插画"), ("立ち絵", "插画"),

        ("資料", "文档"), ("漫画", "文档"), ("小説", "文档"), ("本", "文档"),
        ("book", "文档"), ("comic", "文档"), ("novel", "文档"),
    ];

    public static string FromBoothLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        foreach (var (needle, category) in BoothLabels)
            if (label.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return category;

        return string.Empty;
    }

    public static string Guess(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Other;

        var lower = name.ToLowerInvariant();
        foreach (var (word, category) in Keywords)
            if (lower.Contains(word, StringComparison.OrdinalIgnoreCase))
                return category;

        return Other;
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var lower = raw.ToLowerInvariant();
        lower = VersionTokenRegex().Replace(lower, " ");

        var builder = new StringBuilder(lower.Length);
        foreach (var ch in lower)
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);

        return builder.ToString();
    }

    public static HashSet<string> Hashtags(string? title)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(title)) return tags;

        foreach (Match match in HashtagRegex().Matches(title))
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            if (tag.Length >= 2) tags.Add(tag);
        }

        return tags;
    }

    public static HashSet<string> QueryTokens(string? raw)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return tokens;

        foreach (var part in TokenSplitRegex().Split(raw))
        {
            var token = part.Trim().ToLowerInvariant();
            if (token.Length >= 3) tokens.Add(token);
        }

        return tokens;
    }

    public static string? LeadingSegment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        var head = parts[0].Trim();
        var normalized = Normalize(head);

        return normalized.Length >= 3 && normalized != Normalize(raw) ? head : null;
    }

    public static bool ContainsKeyword(string? title, string? token)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(token)) return false;

        var needle = token.Trim().ToLowerInvariant();
        if (needle.Length < 2) return false;

        if (title.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;

        var normalized = Normalize(needle);
        if (normalized.Length >= 2 && Normalize(title).Contains(normalized, StringComparison.Ordinal))
            return true;

        var kana = ToHiragana(normalized);
        if (kana.Length >= 2)
        {
            if (title.Contains(kana, StringComparison.Ordinal)) return true;
            if (title.Contains(ToKatakana(kana), StringComparison.Ordinal)) return true;
        }

        return InitialsMatch(title, normalized);
    }

    public static bool IsDistinctive(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        return QueryTokens(query).Count >= 2 || Normalize(query).Length >= 8;
    }

    private static string ToHiragana(string romaji)
    {
        if (romaji.Length == 0) return string.Empty;

        var builder = new StringBuilder();

        for (var i = 0; i < romaji.Length;)
        {
            if (char.IsDigit(romaji[i])) return string.Empty;

            if (i + 1 < romaji.Length && romaji[i] == romaji[i + 1] &&
                !IsVowel(romaji[i]) && romaji[i] is not 'n' and not 'm')
            {
                builder.Append('っ');
                i++;
                continue;
            }

            if (romaji[i] == 'n' && (i + 1 == romaji.Length || !IsVowel(romaji[i + 1]) && romaji[i + 1] != 'y'))
            {
                builder.Append('ん');
                i++;
                continue;
            }

            var matched = false;

            for (var length = Math.Min(3, romaji.Length - i); length >= 1; length--)
            {
                if (!KanaSyllables.TryGetValue(romaji.Substring(i, length), out var kana)) continue;

                builder.Append(kana);
                i += length;
                matched = true;
                break;
            }

            if (!matched) return string.Empty;
        }

        return builder.ToString();
    }

    private static bool IsVowel(char value) => value is 'a' or 'i' or 'u' or 'e' or 'o';

    private static string ToKatakana(string hiragana)
    {
        var builder = new StringBuilder(hiragana.Length);

        foreach (var ch in hiragana)
            builder.Append(ch is >= '\u3041' and <= '\u3096' ? (char)(ch + 0x60) : ch);

        return builder.ToString();
    }

    private static readonly Dictionary<string, string> KanaSyllables = new(StringComparer.Ordinal)
    {
        ["a"] = "あ", ["i"] = "い", ["u"] = "う", ["e"] = "え", ["o"] = "お",
        ["ka"] = "か", ["ki"] = "き", ["ku"] = "く", ["ke"] = "け", ["ko"] = "こ",
        ["sa"] = "さ", ["shi"] = "し", ["su"] = "す", ["se"] = "せ", ["so"] = "そ",
        ["ta"] = "た", ["chi"] = "ち", ["tsu"] = "つ", ["te"] = "て", ["to"] = "と",
        ["na"] = "な", ["ni"] = "に", ["nu"] = "ぬ", ["ne"] = "ね", ["no"] = "の",
        ["ha"] = "は", ["hi"] = "ひ", ["fu"] = "ふ", ["he"] = "へ", ["ho"] = "ほ",
        ["ma"] = "ま", ["mi"] = "み", ["mu"] = "む", ["me"] = "め", ["mo"] = "も",
        ["ya"] = "や", ["yu"] = "ゆ", ["yo"] = "よ",
        ["ra"] = "ら", ["ri"] = "り", ["ru"] = "る", ["re"] = "れ", ["ro"] = "ろ",
        ["wa"] = "わ", ["wo"] = "を", ["n"] = "ん",
        ["ga"] = "が", ["gi"] = "ぎ", ["gu"] = "ぐ", ["ge"] = "げ", ["go"] = "ご",
        ["za"] = "ざ", ["ji"] = "じ", ["zu"] = "ず", ["ze"] = "ぜ", ["zo"] = "ぞ",
        ["da"] = "だ", ["de"] = "で", ["do"] = "ど",
        ["ba"] = "ば", ["bi"] = "び", ["bu"] = "ぶ", ["be"] = "べ", ["bo"] = "ぼ",
        ["pa"] = "ぱ", ["pi"] = "ぴ", ["pu"] = "ぷ", ["pe"] = "ぺ", ["po"] = "ぽ",
        ["kya"] = "きゃ", ["kyu"] = "きゅ", ["kyo"] = "きょ",
        ["sha"] = "しゃ", ["shu"] = "しゅ", ["sho"] = "しょ",
        ["cha"] = "ちゃ", ["chu"] = "ちゅ", ["cho"] = "ちょ",
        ["nya"] = "にゃ", ["nyu"] = "にゅ", ["nyo"] = "にょ",
        ["hya"] = "ひゃ", ["hyu"] = "ひゅ", ["hyo"] = "ひょ",
        ["mya"] = "みゃ", ["myu"] = "みゅ", ["myo"] = "みょ",
        ["rya"] = "りゃ", ["ryu"] = "りゅ", ["ryo"] = "りょ",
        ["gya"] = "ぎゃ", ["gyu"] = "ぎゅ", ["gyo"] = "ぎょ",
        ["ja"] = "じゃ", ["ju"] = "じゅ", ["jo"] = "じょ",
        ["bya"] = "びゃ", ["byu"] = "びゅ", ["byo"] = "びょ",
        ["pya"] = "ぴゃ", ["pyu"] = "ぴゅ", ["pyo"] = "ぴょ",
        ["fa"] = "ふぁ", ["fi"] = "ふぃ", ["fe"] = "ふぇ", ["fo"] = "ふぉ",
        ["wi"] = "うぃ", ["we"] = "うぇ", ["she"] = "しぇ", ["che"] = "ちぇ", ["je"] = "じぇ"
    };

    private static bool InitialsMatch(string title, string needle)
    {
        if (needle.Length < 2) return false;

        var initials = new StringBuilder();

        foreach (var word in Words(title))
        {
            initials.Append(char.ToLowerInvariant(word[0]));

            if (initials.Length > needle.Length) return false;
            if (initials.Length == needle.Length) return string.Equals(initials.ToString(), needle, StringComparison.Ordinal);
        }

        return false;
    }

    private static IEnumerable<string> Words(string title)
    {
        var word = new StringBuilder();

        foreach (var ch in title)
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
                continue;
            }

            foreach (var part in SplitCamel(word.ToString())) yield return part;
            word.Clear();
        }

        foreach (var part in SplitCamel(word.ToString())) yield return part;
    }

    private static IEnumerable<string> SplitCamel(string word)
    {
        if (word.Length == 0) yield break;

        var start = 0;

        for (var i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]) && char.IsLower(word[i - 1]))
            {
                yield return word[start..i];
                start = i;
            }
        }

        yield return word[start..];
    }

    public static double Score(string? query, string? title, string? shop)
    {
        var q = Normalize(query);
        var t = Normalize(CleanTitle(title ?? string.Empty));
        if (q.Length < 3 || t.Length < 3) return 0;

        double score;
        if (q == t) score = 1.0;
        else if (t.Contains(q, StringComparison.Ordinal))
            score = t.EndsWith(q, StringComparison.Ordinal) || (double)q.Length / t.Length >= 0.7 ? 0.86 : 0.5;
        else if (q.Contains(t, StringComparison.Ordinal))
            score = t.Length >= 5 && (double)t.Length / q.Length >= 0.3 ? 0.84 : 0.5;
        else score = Overlap(q, t) * 0.8;

        var brand = Normalize(shop);
        if (brand.Length >= 3 && (q.Contains(brand, StringComparison.Ordinal) || Overlap(q, brand) > 0.7))
            score += 0.06;

        return Math.Min(score, 1.0);
    }

    private static string CleanTitle(string title)
    {
        var text = BracketRegex().Replace(title, " ");

        foreach (var separator in new[] { " / ", " | ", " - " })
        {
            var cut = text.IndexOf(separator, StringComparison.Ordinal);
            if (cut > 0) text = text[..cut];
        }

        return text;
    }

    private static double Overlap(string a, string b)
    {
        var left = Grams(a);
        var right = Grams(b);
        if (left.Count == 0 || right.Count == 0) return 0;

        var shared = left.Count(g => right.Contains(g));
        return (double)shared / Math.Max(left.Count, right.Count);
    }

    private static HashSet<string> Grams(string value)
    {
        var grams = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < value.Length; i++)
            grams.Add(value.Substring(i, 2));
        return grams;
    }

    [GeneratedRegex(@"\d+(?:\.\d+){1,3}(?:[-+][0-9a-z.]+)?")]
    private static partial Regex VersionTokenRegex();

    [GeneratedRegex(@"[【\[（(「][^】\]）)」]*[】\]）)」]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"[^0-9A-Za-z\u3040-\u30ff\u4e00-\u9fff]+")]
    private static partial Regex TokenSplitRegex();

    [GeneratedRegex(@"#([0-9A-Za-z_\-]{2,})")]
    private static partial Regex HashtagRegex();
}
