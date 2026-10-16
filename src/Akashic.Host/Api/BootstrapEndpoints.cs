using System.Reflection;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// Everything the shell needs before it can draw its first frame: which account is
/// signed in, where the folders are, and how many projects the nav badge should show.
/// </summary>
public static class BootstrapEndpoints
{
    private static readonly Lazy<string> HostVersion = new(() =>
        typeof(BootstrapEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BootstrapEndpoints).Assembly.GetName().Version?.ToString()
        ?? "0.0.0");

    public static RouteGroupBuilder MapBootstrap(this RouteGroupBuilder group)
    {
        group.MapGet("/info", () => new
        {
            kind = "akashic-host",
            version = HostVersion.Value,
            pid = Environment.ProcessId,
            dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic"),
        });

        group.MapGet("/state", async (IProjectService projects, IVpmRegistryService registry,
            IBoothService booth, IAssetStoreService assetStore, IVRChatSyncService vrchat,
            IThemeService theme, IRailLayoutService rail, CancellationToken ct) =>
        {
            var list = await projects.GetProjectsAsync();

            return new StateSnapshot(
                ProjectCount: list.Count,
                DefaultProjectPath: projects.GetDefaultProjectPath(),
                BackupDirectory: projects.GetBackupDirectory(),
                BackupExcludesPackages: projects.BackupExcludesPackages,
                Editors: projects.DiscoverUnityEditors(),
                RegistrySources: registry.GetRegistryUrls(),
                Repositories: registry.GetKnownRepositories(),
                Booth: new SessionSnapshot(booth.HasSession, booth.GetDownloadDirectory()),
                AssetStore: new SessionSnapshot(assetStore.HasSession, assetStore.GetCacheDirectory()),
                VRChat: new SessionSnapshot(vrchat.HasSession, null),
                AssetStoreDownloadDirectory: assetStore.GetDownloadDirectory(),
                DarkTheme: theme.IsDarkTheme,
                SheetOpacity: theme.SheetOpacity,
                Rail: rail.Items);
        });

        // The rail is the one piece of shell layout worth remembering across runs.
        group.MapPost("/rail", async Task<IResult> (RailLayout layout, IRailLayoutService rail,
            CancellationToken ct) =>
        {
            await rail.SetItemsAsync(layout.Items, ct);
            return Results.Json(new { items = rail.Items });
        });

        // Only Booth can be asked cheaply whether a stored session still works; Unity and
        // VRChat answer 200 for anonymous requests too, so those stay "restored".
        group.MapPost("/sessions/probe", async (IBoothService booth, CancellationToken ct) =>
            new SessionProbe(booth.HasSession, await booth.ProbeSessionAsync(ct)));

        group.MapPost("/theme", async (ThemeChoice choice, IThemeService theme, CancellationToken ct) =>
        {
            await theme.SetDarkThemeAsync(choice.Dark, ct);
            return new { dark = theme.IsDarkTheme };
        });

        // Alpha of the glass surfaces. Answered with what was actually stored, because the
        // service clamps and the slider has to snap back to the allowed range.
        group.MapPost("/visual", async Task<IResult> (VisualSettings visual, IThemeService theme,
            CancellationToken ct) =>
        {
            await theme.SetVisualAsync(visual.Opacity, ct);
            return Results.Json(new { opacity = theme.SheetOpacity });
        });

        return group;
    }
}

public sealed record StateSnapshot(
    int ProjectCount,
    string DefaultProjectPath,
    string BackupDirectory,
    bool BackupExcludesPackages,
    List<UnityEditorEntry> Editors,
    List<string> RegistrySources,
    IReadOnlyList<VccRepository> Repositories,
    SessionSnapshot Booth,
    SessionSnapshot AssetStore,
    SessionSnapshot VRChat,
    string AssetStoreDownloadDirectory,
    bool DarkTheme,
    double SheetOpacity,
    IReadOnlyList<string> Rail);

public sealed record RailLayout(List<string> Items);

public sealed record SessionSnapshot(bool SignedIn, string? Directory);

public sealed record SessionProbe(bool HasStoredSession, bool? StillValid);

public sealed record ThemeChoice(bool Dark);

public sealed record VisualSettings(double Opacity);
