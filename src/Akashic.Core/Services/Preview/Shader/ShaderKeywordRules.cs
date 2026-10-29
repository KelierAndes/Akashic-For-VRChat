namespace Akashic.Core.Services.Preview.Shader;

public static class ShaderKeywordRules
{
    public const int MaxKeywordLength = 96;

    public static bool IsValid(string keyword) =>
        keyword.Length is > 0 and < MaxKeywordLength && keyword.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static string Key(IEnumerable<string> keywords) => string.Join(",", keywords
        .Where(IsValid)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal));
}
