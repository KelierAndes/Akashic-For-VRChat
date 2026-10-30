using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

public class ResourceMetaEntry
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("coverUrl")]
    public string CoverUrl { get; set; } = string.Empty;

    [JsonPropertyName("boothId")]
    public string BoothId { get; set; } = string.Empty;

    [JsonPropertyName("boothTitle")]
    public string BoothTitle { get; set; } = string.Empty;

    [JsonPropertyName("boothShop")]
    public string BoothShop { get; set; } = string.Empty;

    [JsonPropertyName("assetStoreName")]
    public string AssetStoreName { get; set; } = string.Empty;

    [JsonPropertyName("manualMatch")]
    public bool ManualMatch { get; set; }

    [JsonPropertyName("matchedAt")]
    public DateTime? MatchedAt { get; set; }

    [JsonPropertyName("attemptedAt")]
    public DateTime? AttemptedAt { get; set; }

    [JsonPropertyName("editedAt")]
    public DateTime? EditedAt { get; set; }

    [JsonIgnore]
    public bool HasMatch => BoothId.Length > 0 || AssetStoreName.Length > 0;

    [JsonIgnore]
    public bool IsEdited => EditedAt is not null;
}

public class ResourceMetaCache
{
    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("entries")]
    public Dictionary<string, ResourceMetaEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ResourceRow
{
    public string Key { get; set; } = string.Empty;

    public string MetaKey { get; set; } = string.Empty;

    public string Kind { get; set; } = "unitypackage";

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime LastModified { get; set; }

    public string Origin { get; set; } = nameof(PackageOrigin.Local);

    public string CoverUrl { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string SourceUrl { get; set; } = string.Empty;

    public bool Matched { get; set; }

    public string MatchSource { get; set; } = string.Empty;

    public string BoothId { get; set; } = string.Empty;

    public string BoothShop { get; set; } = string.Empty;

    public string OriginLabel { get; set; } = string.Empty;

    public string ArchivePath { get; set; } = string.Empty;

    public string ArchiveEntry { get; set; } = string.Empty;

    public string ContainerPath { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public int ChildCount { get; set; }

    public bool IsManual { get; set; }

    public bool ManualMatch { get; set; }

    public bool Attempted { get; set; }

    public List<ResourceRow> Children { get; set; } = [];

    public bool IsContainer => Kind is "archive" or "folder";

    public bool IsMatchable => Kind == "unitypackage";

    public bool CanMatch => IsContainer ? Children.Count > 0 : IsMatchable;
}

public class BoothMatchCandidate
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Shop { get; set; } = string.Empty;

    public string ShopUrl { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public string CoverUrl { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public double Score { get; set; }
}

public class PendingMatch
{
    [JsonPropertyName("metaKey")]
    public string MetaKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("containerKey")]
    public string ContainerKey { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("candidates")]
    public List<BoothMatchCandidate> Candidates { get; set; } = [];
}

public class ResourceMatchStatus
{
    public bool Running { get; set; }

    public int Total { get; set; }

    public int Scanned { get; set; }

    public int Matched { get; set; }

    public int Failed { get; set; }

    public int Pending { get; set; }

    public string Current { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool Cancelled { get; set; }
}

public sealed record ResourceMetaUpdate(
    string Key,
    string? Category,
    List<string>? Tags,
    string? SourceUrl,
    string? CoverUrl);

public sealed record ResourceMatchRequest(bool Force = false);

public sealed record PendingMatchRequest(string Key, string BoothId);

public sealed record ResourceKeyRequest(string Key);

public sealed record ArchiveExtractRequest(string ArchivePath, List<string>? Entries);
