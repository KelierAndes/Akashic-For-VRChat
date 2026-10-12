using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// One Unity editor this machine knows about: which version it is, where the exe lives, and who
/// reported it. Only 手动添加 entries are persisted; the rest are re-discovered on every refresh.
/// </summary>
public class UnityEditorEntry
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>手动添加 / Unity Hub / VCC / 扫描目录.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "手动添加";

    [JsonIgnore]
    public bool IsManual => Source == "手动添加";
}
