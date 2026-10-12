namespace Akashic.Core.Services;

/// <summary>
/// One cookie as the browser actually holds it. The domain is part of the identity, not decoration:
/// a session cookie belongs to the host that set it, and putting it anywhere else means the site
/// never sees it. <see cref="Domain"/> keeps Chromium's own spelling — a leading dot for a domain
/// cookie, a bare host for a host-only one.
/// </summary>
public sealed record StoredCookie(string Name, string Value, string Domain, string Path = "/");

/// <summary>A stored session with the time it was captured, so the UI can say how old it is.</summary>
public sealed record StoredSession(IReadOnlyList<StoredCookie> Cookies, DateTime SavedAt);

/// <summary>
/// Keeps the Booth / Asset Store / VRChat sessions on disk so a WebView2 profile reset stops wiping
/// every login. See <see cref="DpapiSessionVault"/> for what the encryption does and does not buy.
/// </summary>
public interface ISessionVault
{
    /// <summary>The stored session for a site, or null when there is none or it cannot be read.</summary>
    StoredSession? Load(string site);

    void Save(string site, IReadOnlyList<StoredCookie> cookies);

    void Clear(string site);
}

/// <summary>Shared shaping of a cookie set, which all three storefront services need twice.</summary>
public static class StoredCookies
{
    /// <summary>
    /// The name/value view a hand-built <c>Cookie:</c> header needs. Such a header carries no domain,
    /// so two cookies of the same name on different hosts collapse to one — the last one wins, which
    /// is what the flattened export always produced.
    /// </summary>
    public static Dictionary<string, string> ToNameValue(IEnumerable<StoredCookie> cookies) =>
        cookies
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
}
