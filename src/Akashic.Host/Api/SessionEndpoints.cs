using Akashic.Core.Services;

namespace Akashic.Host.Api;

public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessions(this RouteGroupBuilder group)
    {
        group.MapGet("/sessions/{site}/cookies",
            (string site, IBoothService booth, IAssetStoreService store, IVRChatSyncService vrchat) =>
        {
            var session = For(site, booth, store, vrchat);
            return session is null
                ? Results.Problem("未知站点。", statusCode: 404)
                : Results.Json(new
                {
                    cookies = session.Value.Entries ?? Array.Empty<StoredCookie>(),
                    signedIn = session.Value.HasSession,
                });
        });

        group.MapPost("/sessions/{site}/cookies",
            (string site, SessionCookies body, IBoothService booth, IAssetStoreService store,
                IVRChatSyncService vrchat) =>
        {
            var session = For(site, booth, store, vrchat);
            if (session is null) return Results.Problem("未知站点。", statusCode: 404);

            var entries = body.Cookies
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Domain))
                .Select(c => new StoredCookie(c.Name, c.Value, c.Domain, c.Path ?? "/"))
                .ToList();

            session.Value.Import(entries);
            return Results.Json(new { accepted = entries.Count, signedIn = session.Value.HasSession });
        });

        return group;
    }

    private static (IReadOnlyList<StoredCookie>? Entries,
        Action<IReadOnlyList<StoredCookie>> Import,
        bool HasSession)? For(string site, IBoothService booth, IAssetStoreService store,
        IVRChatSyncService vrchat) => site.ToLowerInvariant() switch
    {
        "booth" => (booth.SessionEntries, booth.ImportSession, booth.HasSession),
        "assetstore" => (store.SessionEntries, store.ImportSession, store.HasSession),
        "vrchat" => (vrchat.SessionEntries, vrchat.ImportSession, vrchat.HasSession),
        _ => null,
    };
}

public sealed record CookieDraft(string Name, string Value, string Domain, string? Path);

public sealed record SessionCookies(List<CookieDraft> Cookies);
