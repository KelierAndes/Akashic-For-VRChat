using Akashic.Core.Services;
using Xunit;

namespace Akashic.Tests;

public sealed class BoothMatchTests
{
    [Fact]
    public void LeadingUnderscoreSegmentIsUsedWhenTheNamePacksProductAndAvatarTogether()
    {
        Assert.Equal("school elegan", PackageCategory.LeadingSegment("school elegan_shinano_v1.7"));
        Assert.Null(PackageCategory.LeadingSegment("SinglePackage"));
        Assert.Null(PackageCategory.LeadingSegment("a_b"));
    }

    [Fact]
    public void SegmentScoresHighAgainstTheBoothProductTitle()
    {
        Assert.True(PackageCategory.Score("school elegan", "School Elegant", "") >= 0.8);
        Assert.True(PackageCategory.Score("school elegan", "【12アバター対応】スクールエレガン", "") < 0.8);
    }

    [Theory]
    [InlineData("Shinano Outfit", "shinano")]
    [InlineData("しなの専用 ロリ化Prefab", "shinano")]
    [InlineData("シナノ用 衣装", "shinano")]
    [InlineData("AvatarBasicTool", "abt")]
    [InlineData("【ABT】アバターツール", "abt")]
    [InlineData("Nijiiro ちゃん", "nijiiro")]
    public void KeywordsMatchAcrossScriptsAndAbbreviations(string title, string token) =>
        Assert.True(PackageCategory.ContainsKeyword(title, token));

    [Fact]
    public void GenericTextIsNotAKeywordHit() =>
        Assert.False(PackageCategory.ContainsKeyword("Midnight Sailor", "shinano"));

    [Fact]
    public void DistinctiveSegmentQueriesNeedMoreThanOneShortWord()
    {
        Assert.True(PackageCategory.IsDistinctive("school elegan"));
        Assert.True(PackageCategory.IsDistinctive("Avatarbasictool"));
        Assert.False(PackageCategory.IsDistinctive("Shinano"));
    }

    [Fact]
    public void ItemJsonCarriesTagsCategoryAndCover()
    {
        const string json = """
            {
              "id": 7657840,
              "name": "もちふぃった～",
              "category": { "id": 215, "name": "3D衣装" },
              "images": [
                { "original": "https://example.test/a.jpg", "resized": "https://example.test/a_72.jpg" }
              ],
              "shop": { "name": "Nine Gates", "url": "https://yamirin.booth.pm/" },
              "tags": [ { "name": "衣装" }, { "name": "VRChat" } ]
            }
            """;

        var candidate = BoothHtml.ParseItemJson(json, "7657840");

        Assert.NotNull(candidate);
        Assert.Equal("もちふぃった～", candidate!.Title);
        Assert.Equal("服饰", candidate.Category);
        Assert.Equal("Nine Gates", candidate.Shop);
        Assert.Equal("https://yamirin.booth.pm/", candidate.ShopUrl);
        Assert.Equal("https://example.test/a.jpg", candidate.CoverUrl);
        Assert.Equal(["衣装", "VRChat"], candidate.Tags);
    }
}
