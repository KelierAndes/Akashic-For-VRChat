using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Akashic.Core.Services;
using Akashic.ViewModels;

namespace Akashic;

/// <summary>
/// Application entry point with DI container setup.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");

    public App()
    {
        MigrateLegacyData();

        // An unpackaged app has to point WebView2 at a profile of its own, otherwise the Booth
        // login would be stored next to the executable and lost on the next publish.
        var webViewDataFolder = Path.Combine(DataDirectory, "WebView2");
        Directory.CreateDirectory(webViewDataFolder);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataFolder);

        UnhandledException += (s, e) =>
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[UnhandledException] {e.Message}\n{e.Exception}\nStackTrace:\n{e.Exception?.StackTrace}");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[AppDomainUnhandledException] {e.ExceptionObject}");
        };

        InitializeComponent();
        ConfigureServices();
    }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core services
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IVpmRegistryService, VpmRegistryService>();
        services.AddSingleton<IUnityPackageExtractor, UnityPackageExtractor>();
        services.AddSingleton<IUnityPackageLibraryService, UnityPackageLibraryService>();
        services.AddSingleton<IExternalPackageService, ExternalPackageService>();
        services.AddSingleton<IPackageInstaller, PackageInstaller>();
        services.AddSingleton<IBoothService, BoothService>();
        services.AddSingleton<IAssetStoreService, AssetStoreService>();
        services.AddSingleton<IUnityPackageIconService, UnityPackageIconService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // ViewModels
        services.AddTransient<ProjectsViewModel>();
        services.AddTransient<ProjectDetailViewModel>();
        services.AddTransient<PackageBrowserViewModel>();
        services.AddTransient<ImportPackageViewModel>();
        services.AddSingleton<ResourcesViewModel>();
        services.AddSingleton<BoothViewModel>();
        services.AddSingleton<AssetStoreViewModel>();

        Services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Carries the pre-rename %LOCALAPPDATA%\VPMManager folder over to the new one so the settings,
    /// Booth downloads and the WebView2 login profile survive the move. The old folder is left alone.
    /// </summary>
    private static void MigrateLegacyData()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VPMManager");

        try
        {
            if (!Directory.Exists(DataDirectory) && Directory.Exists(legacy))
                CopyDirectory(legacy, DataDirectory);

            // Paths recorded before the rename still point at the old folder.
            var settings = Path.Combine(DataDirectory, "settings.json");
            if (!File.Exists(settings)) return;

            var raw = File.ReadAllText(settings);
            var moved = raw
                .Replace(legacy.Replace("\\", "\\\\"), DataDirectory.Replace("\\", "\\\\"))
                .Replace(legacy, DataDirectory);
            if (moved != raw) File.WriteAllText(settings, moved);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[OnLaunchedException] {ex.Message}\n{ex}\nStackTrace:\n{ex.StackTrace}");
            throw;
        }
    }
}
