using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IThemeService
{
    /// <summary>The persisted preference; defaults to dark.</summary>
    bool IsDarkTheme { get; }

    /// <summary>Persists the preference. Reads settings.json afresh so edits made by the other
    /// services sharing the file are not clobbered.</summary>
    Task SetDarkThemeAsync(bool dark, CancellationToken ct = default);

    /// <summary>Alpha of the glass surfaces — window sheet, rail trays and popups.
    /// The shell reads it at start-up and repaints immediately, so a slider never
    /// needs a restart.</summary>
    double SheetOpacity { get; }

    Task SetVisualAsync(double opacity, CancellationToken ct = default);
}

/// <summary>
/// Stores the light/dark preference in the shared settings.json, following the same
/// read-merge-write pattern as the other services so concurrent edits survive.
/// </summary>
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

    public ThemeService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        var settings = LoadSettings();
        IsDarkTheme = settings.UseDarkTheme;
        // Clamped on the way in as well: a file written by an older build, or by hand, must not
        // put the sheet outside the band the slider can express.
        SheetOpacity = ClampOpacity(settings.SheetOpacity);
    }

    public async Task SetDarkThemeAsync(bool dark, CancellationToken ct = default)
    {
        // Read the full settings afresh and write them back with only the theme changed, so
        // sections owned by the other services survive.
        var settings = LoadSettings();
        settings.UseDarkTheme = dark;
        IsDarkTheme = dark;

        await WriteAsync(settings, ct);
    }

    public async Task SetVisualAsync(double opacity, CancellationToken ct = default)
    {
        // Clamped here rather than trusted from the shell: a NaN would otherwise be
        // written down and leave the window unreadable on the next start. The slider shows
        // this alpha as-is, so the band is 0.4 … 1.
        SheetOpacity = ClampOpacity(opacity);

        var settings = LoadSettings();
        settings.SheetOpacity = SheetOpacity;

        await WriteAsync(settings, ct);
    }

    /// <summary>Keeps a value out of settings.json that the page cannot express any more.</summary>
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
