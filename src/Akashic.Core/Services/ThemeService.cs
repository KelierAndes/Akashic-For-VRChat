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

    public ThemeService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        IsDarkTheme = LoadSettings().UseDarkTheme;
    }

    public async Task SetDarkThemeAsync(bool dark, CancellationToken ct = default)
    {
        // Read the full settings afresh and write them back with only the theme changed, so
        // sections owned by the other services survive.
        var settings = LoadSettings();
        settings.UseDarkTheme = dark;

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(settings, SerializerOptions), ct);
        IsDarkTheme = dark;
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
