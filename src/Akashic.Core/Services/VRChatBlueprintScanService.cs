using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public sealed partial class VRChatBlueprintScanService : IVRChatBlueprintScanService
{
    private static readonly object Gate = new();

    private readonly Dictionary<string, (FileStamp Stamp, List<string> Ids)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IVRChatSyncService _content;
    private bool _cloudCheckedOnce;
    private string[] _cloudIds = [];

    public VRChatBlueprintScanService(IVRChatSyncService content)
    {
        _content = content;
    }

    public IReadOnlyList<string> CloudIds
    {
        get
        {
            EnsureCloudIds();
            lock (Gate) return _cloudIds;
        }
    }

    private void EnsureCloudIds()
    {
        lock (Gate)
        {
            if (_cloudIds.Length > 0 || _cloudCheckedOnce) return;
            _cloudCheckedOnce = true;
        }

        try
        {
            var cached = _content.LoadContentCache();
            if (cached.HasContent) PublishCloudIds(cached.Items.Select(item => item.Id));
        }
        catch (Exception)
        {
        }
    }

    public void PublishCloudIds(IEnumerable<string> blueprintIds)
    {
        var ids = blueprintIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (Gate) _cloudIds = ids;
    }

    public Task<IReadOnlyList<string>> ScanAsync(UnityProject project, bool force = false, CancellationToken ct = default)
    {
        var assets = project.AssetsPath;
        if (!Directory.Exists(assets)) return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<string>();

            foreach (var scene in EnumerateScenes(assets))
            {
                ct.ThrowIfCancellationRequested();

                foreach (var id in ReadScene(scene, force))
                    if (seen.Add(id)) found.Add(id);
            }

            return found;
        }, ct);
    }

    public async Task<Dictionary<string, UnityProject>> MapByBlueprintAsync(
        IReadOnlyList<UnityProject> projects, CancellationToken ct = default)
    {
        var map = new Dictionary<string, UnityProject>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            foreach (var id in await ScanAsync(project, force: false, ct))
            {
                map.TryAdd(id, project);
            }
        }

        return map;
    }

    private static IEnumerable<string> EnumerateScenes(string assets)
    {
        string[] patterns = ["*.unity", "*.prefab", "*.asset"];

        foreach (var pattern in patterns)
        {
            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(assets, pattern, SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files) yield return file;
        }
    }

    private IReadOnlyList<string> ReadScene(string path, bool force)
    {
        var stamp = ReadStamp(path);
        if (stamp is null) return [];

        lock (Gate)
        {
            if (!force && _cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Ids;
        }

        var ids = new List<string>();

        try
        {
            if (stamp.Value.Length <= MaxScanBytes)
            {
                foreach (Match match in BlueprintToken().Matches(File.ReadAllText(path)))
                {
                    var id = match.Value;

                    if (ids.Count < MaxIdsPerFile && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                        ids.Add(id);
                }
            }
        }
        catch (Exception)
        {
        }

        lock (Gate) _cache[path] = (stamp.Value, ids);
        return ids;
    }

    private static FileStamp? ReadStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\b(avtr|wrld)_[0-9a-fA-F-]{36}\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BlueprintToken();

    private const long MaxScanBytes = 128L * 1024 * 1024;

    private const int MaxIdsPerFile = 16;

    private readonly record struct FileStamp(DateTime ModifiedUtc, long Length);
}
