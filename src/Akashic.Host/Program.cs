using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Akashic.Core.Services;
using Akashic.Core.Services.Preview;
using Akashic.Host;
using Akashic.Host.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var options = SidecarOptions.Read(args);

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
builder.Logging.ClearProviders();
var dataRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");
PreviewCachePaths.Configure(ReadPreviewCacheSetting(Path.Combine(dataRoot, "settings.json")));
var fileLogging = new SidecarFileLoggerProvider(Path.Combine(dataRoot, "logs", "host.log"));
builder.Logging.AddProvider(fileLogging);
builder.WebHost.ConfigureKestrel(kestrel =>
{
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
builder.Services.AddSingleton<Akashic.Core.Data.AkashicDatabase>();

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
builder.Services.AddSingleton<IResourceCatalogService, ResourceCatalogService>();
builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddSingleton<IRailLayoutService, RailLayoutService>();
builder.Services.AddSingleton<IVersionControlService, VersionControlService>();
builder.Services.AddSingleton<GitHubCli>();
builder.Services.AddSingleton<GitHubLoopbackAuth>();
builder.Services.AddSingleton<IConfigSyncService, ConfigSyncService>();
builder.Services.AddSingleton<IUpdateService, UpdateService>();
builder.Services.AddSingleton<Akashic.Core.Services.Preview.PackageContentCache>();
builder.Services.AddSingleton<Akashic.Core.Services.Preview.IPreviewService,
    Akashic.Core.Services.Preview.PreviewService>();
builder.Services.AddSingleton<Akashic.Host.Preview.Hlsl.HlslPreviewService>();
builder.Services.AddSingleton<Akashic.Host.Preview.TexturePreviewService>();
builder.Services.AddSingleton<AutoSync>();

var app = builder.Build();

app.UseLoopbackGuard(options);

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        title = "操作失败",
        detail = error?.Message ?? "侧车内部错误。",
    }));
}));

var api = app.MapGroup("/api");
api.MapBootstrap();
api.MapProjects();
api.MapTemplates();
api.MapSettings();
api.MapSessions();
api.MapPackages();
api.MapLibrary();
api.MapResources();
api.MapAccounts();
api.MapRestore();
api.MapGitHub();
api.MapUpdates();
api.MapPreview();
api.MapEvents();

app.MapPreviewRoutes(options);

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<AutoSync>().Queue("启动"));

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
fileLogging.Dispose();

static string? ReadPreviewCacheSetting(string settingsPath)
{
    try
    {
        if (!File.Exists(settingsPath)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (document.RootElement.TryGetProperty("previewCacheDirectory", out var value) &&
            value.ValueKind == JsonValueKind.String)
            return value.GetString();
    }
    catch (Exception)
    {
    }

    return null;
}

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
