namespace Akashic.Core.Services;

public sealed record StoredCookie(string Name, string Value, string Domain, string Path = "/");

public sealed record StoredSession(IReadOnlyList<StoredCookie> Cookies, DateTime SavedAt);

public interface ISessionVault
{
    StoredSession? Load(string site);

    void Save(string site, IReadOnlyList<StoredCookie> cookies);

    void Clear(string site);
}

public static class StoredCookies
{
    public static Dictionary<string, string> ToNameValue(IEnumerable<StoredCookie> cookies) =>
        cookies
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
}
