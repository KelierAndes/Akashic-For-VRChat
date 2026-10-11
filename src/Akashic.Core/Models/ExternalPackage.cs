using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalPackageKind
{
    LocalFolder,
    GitHub,
    GitLab,
    Gitee
}

/// <summary>
/// A UPM package attached outside of VPM: a local package folder or a GitHub repository.
/// </summary>
public class ExternalPackage
{
    /// <summary>Stable key used by settings.json: kind + normalised location.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>UPM package name. Empty until a GitHub package's package.json has been read.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public ExternalPackageKind Kind { get; set; }

    /// <summary>Absolute folder path, or the repository's https URL.</summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>Branch or tag used for GitHub packages.</summary>
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Who supplied the package; entries written before this existed read back as Local.</summary>
    [JsonPropertyName("origin")]
    public PackageOrigin Origin { get; set; }

    [JsonIgnore]
    public string OriginDisplay => Origin.Display();

    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
