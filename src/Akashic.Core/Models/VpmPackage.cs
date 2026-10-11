using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// Represents a VPM package from a registry.
/// </summary>
public class VpmPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("unity")]
    public string? Unity { get; set; }

    [JsonPropertyName("author")]
    public PackageAuthor? Author { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonPropertyName("vpmDependencies")]
    public Dictionary<string, string>? VpmDependencies { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    [JsonIgnore]
    public Dictionary<string, VpmPackage> Versions { get; set; } = [];

    [JsonIgnore]
    public string SourceRegistry { get; set; } = string.Empty;

    /// <summary>Registry URL that advertises this package, so the 来源 column can name the repository.</summary>
    [JsonIgnore]
    public string SourceRegistryUrl { get; set; } = string.Empty;
}

public class PackageAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class VpmRegistryListing
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("packages")]
    public Dictionary<string, VpmPackageListing> Packages { get; set; } = [];
}

public class VpmPackageListing
{
    [JsonPropertyName("versions")]
    public Dictionary<string, VpmPackage> Versions { get; set; } = [];
}
