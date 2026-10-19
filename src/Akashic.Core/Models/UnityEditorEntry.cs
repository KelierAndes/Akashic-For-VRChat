using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

public class UnityEditorEntry
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "手动添加";

    [JsonIgnore]
    public bool IsManual => Source == "手动添加";
}
