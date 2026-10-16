using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// GitHub CLI status and the encrypted config sync that rides on top of it.
///
/// The identity route deliberately answers a hand-built object rather than the
/// <see cref="GitHubIdentity"/> record: that record carries the OAuth token, and the loopback
/// guard only keeps other local processes out — the token has no business in a JSON body.
///
/// The browser sign-in is here too. Its OAuth App pair is compiled into Akashic, so no route on this
/// box accepts or returns a credential — the surface for it simply does not exist.
/// </summary>
public static class GitHubEndpoints
{
    public static RouteGroupBuilder MapGitHub(this RouteGroupBuilder group)
    {
        group.MapGet("/github/status", async Task<object> (GitHubCli gitHub, CancellationToken ct) =>
        {
            var found = GitHubCli.Find() != null;
            var identity = found ? await gitHub.IdentityAsync(ct) : null;

            return new
            {
                found,
                version = identity?.Version ?? string.Empty,
                signedIn = identity?.SignedIn == true,
                login = identity?.Login ?? string.Empty,
                avatarUrl = identity?.AvatarUrl ?? string.Empty,
                scopes = identity?.Scopes ?? [],
                hasGistScope = identity?.HasGistScope == true
            };
        });

        group.MapGet("/sync/status", async Task<object> (IConfigSyncService sync, CancellationToken ct) =>
            await sync.StatusAsync(ct));

        // Both calls shell out to gh and wait on GitHub, so the shell gives them minutes.
        group.MapPost("/sync/push", async Task<IResult> (IConfigSyncService sync, CancellationToken ct) =>
            Results.Json(await sync.PushAsync(ct)));

        // The 启用 gist 同步 switch. Kept separate from /theme because it gates network writes,
        // not appearance.
        group.MapPost("/sync/toggle", async Task<object> (SyncToggle toggle,
            IConfigSyncService sync, CancellationToken ct) =>
        {
            await sync.SetEnabledAsync(toggle.Enabled, ct);
            return new { enabled = toggle.Enabled };
        });

        // Restoring takes no argument: the sidecar knows its own record and can find the account's
        // backup, so no gist id ever has to be shown, typed, or handed around by the page.
        group.MapPost("/sync/pull", async Task<IResult> (
            IConfigSyncService sync, CancellationToken ct) =>
            Results.Json(await sync.PullAsync(ct)));

        // Waits on a human clicking Authorize in a browser, so this one is allowed to run for minutes.
        // The token itself is never part of the answer: it lands in the gh keyring and stays there.
        group.MapPost("/github/login", async Task<IResult> (
            GitHubLoopbackAuth auth, GitHubCli gitHub, CancellationToken ct) =>
        {
            var (clientId, clientSecret) = GitHubOAuthApp.Resolve();
            var result = await auth.LoginAsync(clientId, clientSecret, ct: ct).ConfigureAwait(false);

            gitHub.Forget();

            // Lets the page tell "this account already has a backup" apart from "let's make the
            // first one" without overwriting anything local on the way.
            var hasBackup = result.Ok && await gitHub.FindGistAsync(ct).ConfigureAwait(false) != null;

            return Results.Json(new
            {
                ok = result.Ok,
                hasBackup,
                userId = result.UserId,
                login = result.Login,
                avatarUrl = result.AvatarUrl,
                error = result.Error,
                guidance = result.Guidance
            });
        });

        return group;
    }

    internal sealed record SyncToggle(bool Enabled);
}
