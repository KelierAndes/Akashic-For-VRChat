using Microsoft.Web.WebView2.Core;
using Akashic.Core.Services;

namespace Akashic.Views;

/// <summary>
/// Pushes a stored session back into a WebView2 so the storefront itself looks signed in after the
/// browser profile is reset, which is what the 我的资产 style reads still depend on.
///
/// Each cookie goes back on the domain it was taken from. That is the whole point: a session cookie
/// written to any other domain is simply never sent to the site, so the browser still looks signed
/// out. They come back as session-scoped cookies, which is what a fresh seed wants — the next launch
/// re-seeds from the vault rather than trusting a stale expiry.
/// </summary>
internal static class WebViewCookieSeed
{
    public static void Apply(CoreWebView2 core, IReadOnlyList<StoredCookie>? cookies)
    {
        if (cookies is null || cookies.Count == 0) return;

        foreach (var entry in cookies)
        {
            try
            {
                var cookie = core.CookieManager.CreateCookie(
                    entry.Name, entry.Value, entry.Domain, entry.Path);

                cookie.IsSecure = true;
                cookie.SameSite = CoreWebView2CookieSameSiteKind.Lax;

                core.CookieManager.AddOrUpdateCookie(cookie);
            }
            catch (Exception)
            {
                // One rejected cookie must not stop the rest of the session being restored.
            }
        }
    }
}
