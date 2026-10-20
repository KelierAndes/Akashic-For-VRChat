using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IThemeService
{
    bool IsDarkTheme { get; }

    Task SetDarkThemeAsync(bool dark, CancellationToken ct = default);

    double SheetOpacity { get; }

    void Reload();

    Task SetVisualAsync(double opacity, CancellationToken ct = default);
}

public class ThemeService : IThemeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public bool IsDarkTheme { get; private set; } = true;

    public double SheetOpacity { get; private set; } = 0.7;

    public void Reload()
    {
        var settings = LoadSettings();
        IsDarkTheme = settings.UseDarkTheme;
        SheetOpacity = ClampOpacity(settings.SheetOpacity);
    }

    public ThemeService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        var settings = LoadSettings();
        IsDarkTheme = settings.UseDarkTheme;
        SheetOpacity = ClampOpacity(settings.SheetOpacity);
    }

    public async Task SetDarkThemeAsync(bool dark, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        settings.UseDarkTheme = dark;
        IsDarkTheme = dark;

        await WriteAsync(settings, ct);
    }

    public async Task SetVisualAsync(double opacity, CancellationToken ct = default)
    {
        SheetOpacity = ClampOpacity(opacity);

        var settings = LoadSettings();
        settings.SheetOpacity = SheetOpacity;

        await WriteAsync(settings, ct);
    }

    private static double ClampOpacity(double opacity) =>
        double.IsFinite(opacity) ? Math.Clamp(opacity, 0.4, 1.0) : 0.7;

    private async Task WriteAsync(AppSettings settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(_settingsPath,
            JsonSerializer.Serialize(settings, SerializerOptions), ct);
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), SerializerOptions)
                       ?? new AppSettings();
        }
        catch (JsonException)
        {
        }

        return new AppSettings();
    }
}
