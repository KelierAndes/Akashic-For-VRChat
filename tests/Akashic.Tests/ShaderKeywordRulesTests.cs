using Akashic.Core.Services.Preview.Shader;
using Xunit;

namespace Akashic.Tests;

public class ShaderKeywordRulesTests
{
    [Theory]
    [InlineData("_ALPHATEST_ON", true)]
    [InlineData("DIRECTIONAL", true)]
    [InlineData("A1_b2", true)]
    [InlineData("", false)]
    [InlineData("_WITH SPACE", false)]
    [InlineData("_WITH-DASH", false)]
    [InlineData("_WITH.SLASH", false)]
    public void ValidatesKeywords(string keyword, bool expected) =>
        Assert.Equal(expected, ShaderKeywordRules.IsValid(keyword));

    [Fact]
    public void RejectsKeywordAtLengthLimit()
    {
        var keyword = new string('A', ShaderKeywordRules.MaxKeywordLength);
        Assert.False(ShaderKeywordRules.IsValid(keyword));
        Assert.True(ShaderKeywordRules.IsValid(keyword[..^1]));
    }

    [Fact]
    public void KeyIsOrderInsensitiveAndFiltersInvalid()
    {
        var left = ShaderKeywordRules.Key(["_B", "_A", "_B", "_A"]);
        var right = ShaderKeywordRules.Key(["_A", "_B", "bad keyword"]);

        Assert.Equal("_A,_B", left);
        Assert.Equal(left, right);
    }
}

public class ShaderCacheKeyTests
{
    [Fact]
    public void SameInputsProduceSameKey() =>
        Assert.Equal(
            ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1234, "_A", true),
            ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1234, "_A", true));

    [Fact]
    public void KeyChangesWithStampKeywordsAndOutline()
    {
        var baseline = ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1234, "_A", true);

        Assert.NotEqual(baseline, ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1235, "_A", true));
        Assert.NotEqual(baseline, ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1234, "_B", true));
        Assert.NotEqual(baseline, ShaderCacheKey.For("shim-v7", @"C:\pkg\a.shader", 1234, "_A", false));
        Assert.NotEqual(baseline, ShaderCacheKey.For("shim-v8", @"C:\pkg\a.shader", 1234, "_A", true));
    }

    [Fact]
    public void KeyIsShortLowercaseHex()
    {
        var key = ShaderCacheKey.For("shim-v7", "a", 1, string.Empty, false);

        Assert.Equal(24, key.Length);
        Assert.Equal(key.ToLowerInvariant(), key);
        Assert.All(key, character => Assert.True(Uri.IsHexDigit(character)));
    }
}
