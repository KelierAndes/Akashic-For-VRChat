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
