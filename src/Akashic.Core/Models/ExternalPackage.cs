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

public class ExternalPackage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public ExternalPackageKind Kind { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public PackageOrigin Origin { get; set; }

    [JsonIgnore]
    public string OriginDisplay => Origin.Display();

    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
