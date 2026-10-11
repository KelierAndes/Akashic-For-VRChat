using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// Represents the VPM manifest file (vpm-manifest.json) in a Unity project.
/// </summary>
public class VpmManifest
{
    [JsonPropertyName("dependencies")]
    public Dictionary<string, VpmDependencyInfo> Dependencies { get; set; } = [];

    [JsonPropertyName("locked")]
    public Dictionary<string, VpmLockedPackage> Locked { get; set; } = [];
}

public class VpmDependencyInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public class VpmLockedPackage
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }
}

/// <summary>
/// Represents the Unity Package Manager manifest (Packages/manifest.json).
/// </summary>
public class UpmManifest
{
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = [];

    [JsonPropertyName("scopedRegistries")]
    public List<ScopedRegistry>? ScopedRegistries { get; set; }
}

public class ScopedRegistry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = [];
}

/// <summary>
/// Represents a package.json inside a package folder.
/// </summary>
public class PackageJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}
