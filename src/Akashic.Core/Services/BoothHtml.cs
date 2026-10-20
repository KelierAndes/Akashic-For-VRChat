using System.Net;
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
