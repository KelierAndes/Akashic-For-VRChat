using Akashic.Core.Services;

namespace Akashic.Host.Api;

public static class UpdateEndpoints
{
    public static RouteGroupBuilder MapUpdates(this RouteGroupBuilder group)
    {
        group.MapGet("/update/check", async Task<IResult> (bool? force, IUpdateService update,
            CancellationToken ct) => Results.Json(await update.CheckAsync(force == true, ct)));

        group.MapGet("/update/state", (IUpdateService update) => Results.Json(update.GetState()));

        group.MapPost("/update/download", async (IUpdateService update, CancellationToken ct) =>
            Results.Json(await update.DownloadAsync(ct)));

        group.MapPost("/update/apply", (UpdateApplyRequest request, IUpdateService update) =>
            Results.Json(new { started = update.Apply(request.Pid) }));

        return group;
    }
}

public sealed record UpdateApplyRequest(int Pid);
