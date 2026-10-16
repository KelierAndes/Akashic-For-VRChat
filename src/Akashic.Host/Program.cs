using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Akashic.Core.Services;
using Akashic.Host;
using Akashic.Host.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var options = SidecarOptions.Read(args);

// The shell spawns this process and reads exactly one line back: the bound url and the
// run token. Anything written to stdout would corrupt that handshake.
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // ListenLocalhost(0) refuses a dynamic port, so name the loopback address itself.
    kestrel.Listen(System.Net.IPAddress.Loopback, options.Port);
});

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<EventHub>();

// The same core graph the WinUI container builds. Only the views move to Flutter; every
// service, and every behaviour verified against them, stays exactly where it is.
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddSingleton<IVpmRegistryService, VpmRegistryService>();
builder.Services.AddSingleton<IUnityPackageExtractor, UnityPackageExtractor>();
builder.Services.AddSingleton<IUnityPackageLibraryService, UnityPackageLibraryService>();
builder.Services.AddSingleton<IExternalPackageService, ExternalPackageService>();
builder.Services.AddSingleton<IPackageInstaller, PackageInstaller>();
builder.Services.AddSingleton<ISessionVault, DpapiSessionVault>();
builder.Services.AddSingleton<IBoothService, BoothService>();
builder.Services.AddSingleton<IAssetStoreService, AssetStoreService>();
builder.Services.AddSingleton<IVRChatSyncService, VRChatSyncService>();
builder.Services.AddSingleton<IVRChatBlueprintScanService, VRChatBlueprintScanService>();
builder.Services.AddSingleton<IUnityPackageIconService, UnityPackageIconService>();
builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddSingleton<IRailLayoutService, RailLayoutService>();
builder.Services.AddSingleton<IVersionControlService, VersionControlService>();
builder.Services.AddSingleton<GitHubCli>();
builder.Services.AddSingleton<GitHubLoopbackAuth>();
builder.Services.AddSingleton<IConfigSyncService, ConfigSyncService>();

var app = builder.Build();

app.UseLoopbackGuard(options);

var api = app.MapGroup("/api");
api.MapBootstrap();
api.MapProjects();
api.MapTemplates();
api.MapSettings();
api.MapSessions();
api.MapPackages();
api.MapLibrary();
api.MapAccounts();
api.MapRestore();
api.MapGitHub();
api.MapEvents();

await app.StartAsync();

var url = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{options.Port}";
Console.Out.WriteLine(Handshake(url, options));
Console.Out.Flush();

if (options.ParentPid is > 0)
{
    WatchParent(app.Lifetime, options.ParentPid.Value);
}

await app.WaitForShutdownAsync();
await app.StopAsync();

static string Handshake(string url, SidecarOptions options)
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    return JsonSerializer.Serialize(new
    {
        kind = "akashic-host",
        url,
        token = options.Token,
        pid = Environment.ProcessId,
        version,
        dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic"),
        startedAt = DateTimeOffset.UtcNow,
    });
}

static void WatchParent(IHostApplicationLifetime lifetime, int parentPid)
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));

            try
            {
                if (Process.GetProcessById(parentPid).HasExited) break;
            }
            catch (ArgumentException)
            {
                break;
            }
        }

        lifetime.StopApplication();
    });
}
