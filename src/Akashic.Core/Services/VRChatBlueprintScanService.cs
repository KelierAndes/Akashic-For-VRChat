using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// The SDK writes the id of whatever a scene was last built as into the scene's own YAML, as a
/// <c>blueprintId:</c> line on its descriptor component — the same field for worlds and avatars, so
/// one pattern covers both. Only the project's <c>Assets</c> folder is read: the SDK's bundled test
/// scenes under <c>Packages</c> carry other people's ids.
/// </summary>
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

    /// <summary>
    /// The cloud list is published by the sync page into memory, which is empty right after a restart
    /// even though that same list has been sitting in the page's disk cache. Reading it back is what
    /// lets the project pages offer the cloud jump without the sync page being opened first.
    /// </summary>
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
            // An unreadable cache just means there is nothing to match against yet.
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
                // Two projects holding the same content means one of them is a copy; the first
                // registered wins rather than guessing.
                map.TryAdd(id, project);
            }
        }

        return map;
    }

    private static IEnumerable<string> EnumerateScenes(string assets)
    {
        // The descriptor travels in scenes and in prefabs alike, so every YAML asset type that can
        // hold one is read. Packages is never touched: the SDK's own test scenes carry other
        // people's ids.
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

    /// <summary>
    /// Grabs every blueprint id token in a file rather than looking for a particular YAML key.
    ///
    /// The SDK writes the id as <c>blueprintId: avtr_…</c> on its own component, but when the
    /// descriptor lives in a prefab and the scene only overrides it, the same value arrives as a
    /// two-line <c>propertyPath: blueprintId</c> / <c>value: avtr_…</c> modification pair — which a
    /// key-shaped pattern silently misses, leaving whole projects unmatched. Matching the token is
    /// safe because only ids the account actually holds are ever kept.
    /// </summary>
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
            // A scene is text, but an unusually huge one is not worth pulling into memory for.
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
            // A scene Unity is still writing, or one we cannot open: nothing to link from it.
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

    // Scene files are routinely tens of megabytes; past this a project is not worth the read.
    private const long MaxScanBytes = 128L * 1024 * 1024;

    // A project that references more content ids than this is a hub of imported assets, and the
    // extra entries would only be noise in the match list.
    private const int MaxIdsPerFile = 16;

    private readonly record struct FileStamp(DateTime ModifiedUtc, long Length);
}
