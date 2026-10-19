using Akashic.Core.Services;

namespace Akashic.Host.Api;

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

        group.MapPost("/sync/push", async Task<IResult> (IConfigSyncService sync, CancellationToken ct) =>
            Results.Json(await sync.PushAsync(ct)));

        group.MapPost("/sync/toggle", async Task<object> (SyncToggle toggle,
            IConfigSyncService sync, CancellationToken ct) =>
        {
            await sync.SetEnabledAsync(toggle.Enabled, ct);
            return new { enabled = toggle.Enabled };
        });

        group.MapPost("/sync/pull", async Task<IResult> (
            IConfigSyncService sync, CancellationToken ct) =>
            Results.Json(await sync.PullAsync(ct)));

        group.MapPost("/github/login", async Task<IResult> (
            GitHubLoopbackAuth auth, GitHubCli gitHub, CancellationToken ct) =>
        {
            var (clientId, clientSecret) = GitHubOAuthApp.Resolve();
            var result = await auth.LoginAsync(clientId, clientSecret, ct: ct).ConfigureAwait(false);

            gitHub.Forget();

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
