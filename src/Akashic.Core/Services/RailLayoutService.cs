using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IRailLayoutService
{
    /// <summary>The tiles the icon rail shows, in order, as <c>nav:&lt;id&gt;</c> or
    /// <c>proj:&lt;path&gt;</c>.</summary>
    IReadOnlyList<string> Items { get; }

    /// <summary>Persists the rail. An empty list is stored as empty, which reads back as
    /// the built-in set.</summary>
    Task SetItemsAsync(IReadOnlyList<string> items, CancellationToken ct = default);
}

/// <summary>
/// Keeps the rail's contents in the shared settings.json, reading the file afresh on every
/// write so the sections owned by the other services survive.
/// </summary>
public class RailLayoutService : IRailLayoutService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public RailLayoutService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        Items = Load().RailItems.Where(Shaped).ToList();
    }

    public IReadOnlyList<string> Items { get; private set; }

    public async Task SetItemsAsync(IReadOnlyList<string> items, CancellationToken ct = default)
    {
        var settings = Load();
        settings.RailItems = items.Where(Shaped).Distinct().ToList();

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(_settingsPath,
            JsonSerializer.Serialize(settings, SerializerOptions), ct);
        Items = settings.RailItems;
    }

    /// <summary>Drops anything that is not one of the two supported shapes.</summary>
    private static bool Shaped(string raw) =>
        raw.StartsWith("nav:") && raw.Length > 4 ||
        raw.StartsWith("proj:") && raw.Length > 6;

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath),
                           SerializerOptions)
                       ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }
}
