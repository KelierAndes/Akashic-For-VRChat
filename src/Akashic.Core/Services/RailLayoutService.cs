using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IRailLayoutService
{
    IReadOnlyList<string> Items { get; }

    void Reload();

    Task SetItemsAsync(IReadOnlyList<string> items, CancellationToken ct = default);
}

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

    public void Reload()
    {
        Items = Load().RailItems.Where(Shaped).ToList();
    }

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
