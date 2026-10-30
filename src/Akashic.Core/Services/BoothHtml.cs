using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public static partial class BoothHtml
{
    public static List<BoothMatchCandidate> ParseCards(string html)
    {
        var result = new List<BoothMatchCandidate>();
        if (string.IsNullOrWhiteSpace(html)) return result;

        foreach (Match card in CardRegex().Matches(html))
        {
            var block = card.Value;

            var id = First(block, ProductIdRegex());
            if (id.Length == 0) continue;

            var title = Text(First(block, CardTitleRegex()));
            if (title.Length == 0) title = WebUtility.HtmlDecode(First(block, ProductNameRegex())).Trim();

            var shop = Text(First(block, ShopNameRegex()));
            if (shop.Length == 0) shop = First(block, ProductBrandRegex());

            result.Add(new BoothMatchCandidate
            {
                Id = id,
                Title = title,
                Shop = shop,
                ShopUrl = First(block, ShopUrlRegex()),
                CategoryId = First(block, ProductCategoryRegex()),
                Category = PackageCategory.FromBoothLabel(Text(First(block, CardCategoryRegex()))),
                CoverUrl = WebUtility.HtmlDecode(First(block, CoverRegex()))
            });
        }

        return result;
    }

    public static BoothMatchCandidate? ParseItem(string html, string id)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var title = CleanTitle(WebUtility.HtmlDecode(Meta(html, "og:title")));
        var shop = WebUtility.HtmlDecode(Meta(html, "og:site_name"));
        if (title.Length == 0) return null;

        return new BoothMatchCandidate
        {
            Id = id,
            Title = title,
            Shop = shop,
            ShopUrl = string.Empty,
            CategoryId = First(html, ProductCategoryRegex()),
            Category = string.Empty,
            CoverUrl = WebUtility.HtmlDecode(Meta(html, "og:image"))
        };
    }

    public static BoothMatchCandidate? ParseItemJson(string json, string id)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var title = WebUtility.HtmlDecode(Str(root, "name")).Trim();
            if (title.Length == 0) return null;

            var cover = string.Empty;
            if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var image in images.EnumerateArray())
                {
                    cover = Str(image, "original");
                    if (cover.Length == 0) cover = Str(image, "resized");
                    if (cover.Length > 0) break;
                }
            }

            var shop = string.Empty;
            var shopUrl = string.Empty;
            if (root.TryGetProperty("shop", out var shopNode) && shopNode.ValueKind == JsonValueKind.Object)
            {
                shop = Str(shopNode, "name");
                shopUrl = Str(shopNode, "url");
            }

            var category = string.Empty;
            if (root.TryGetProperty("category", out var categoryNode) &&
                categoryNode.ValueKind == JsonValueKind.Object)
                category = PackageCategory.FromBoothLabel(Str(categoryNode, "name"));

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagNodes) && tagNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagNodes.EnumerateArray())
                {
                    var name = Str(tag, "name").Trim();
                    if (name.Length > 0) tags.Add(name);
                }
            }

            return new BoothMatchCandidate
            {
                Id = id,
                Title = title,
                Shop = shop.Trim(),
                ShopUrl = shopUrl,
                Category = category,
                CoverUrl = cover,
                Tags = tags
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string CleanTitle(string raw)
    {
        var title = raw.Trim();
        title = Regex.Replace(title, @"\s*-\s*BOOTH\s*$", string.Empty, RegexOptions.IgnoreCase);
        return title.Trim();
    }

    private static string Meta(string html, string name)
    {
        var quoted = Regex.Escape(name);
        var pattern = $@"<meta[^>]+(?:property|name)\s*=\s*""{quoted}""[^>]*content\s*=\s*""([^""]*)""";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        pattern = $@"<meta[^>]+content\s*=\s*""([^""]*)""[^>]*(?:property|name)\s*=\s*""{quoted}""";
        match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string First(string block, Regex regex)
    {
        var match = regex.Match(block);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string Text(string raw)
    {
        if (raw.Length == 0) return string.Empty;
        var stripped = Regex.Replace(raw, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(Regex.Replace(stripped, @"\s+", " ")).Trim();
    }

    [GeneratedRegex(@"<li class=""item-card[\s\S]*?</li>")]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"data-product-id=""(\d+)""")]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(@"data-product-name=""([^""]*)""")]
    private static partial Regex ProductNameRegex();

    [GeneratedRegex(@"data-product-brand=""([^""]*)""")]
    private static partial Regex ProductBrandRegex();

    [GeneratedRegex(@"data-product-category=""(\d*)""")]
    private static partial Regex ProductCategoryRegex();

    [GeneratedRegex(@"item-card__title-anchor[^>]*>([\s\S]*?)</a>")]
    private static partial Regex CardTitleRegex();

    [GeneratedRegex(@"item-card__category-anchor[^>]*>([\s\S]*?)</a>")]
    private static partial Regex CardCategoryRegex();

    [GeneratedRegex(@"item-card__shop-name""[^>]*>([\s\S]*?)<")]
    private static partial Regex ShopNameRegex();

    [GeneratedRegex(@"href=""(https://[^""]+\.booth\.pm/)""")]
    private static partial Regex ShopUrlRegex();

    [GeneratedRegex(@"data-original=""([^""]+)""")]
    private static partial Regex CoverRegex();
}
