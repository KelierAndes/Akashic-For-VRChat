namespace Akashic.Core.Models;

public class ProjectTemplate
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "自定义";

    [System.Text.Json.Serialization.JsonPropertyName("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string CreatedAtDisplay => IsBuiltIn ? "随 Creator Companion" : CreatedAt.ToString("yyyy/MM/dd HH:mm");

    [System.Text.Json.Serialization.JsonIgnore]
    public string NotesDisplay => string.IsNullOrWhiteSpace(Notes) ? "（无说明）" : Notes;

    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceDisplay => IsBuiltIn
        ? "VRChat Creator Companion 自带"
        : string.IsNullOrWhiteSpace(SourcePath)
            ? "未知来源"
            : System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(SourcePath));

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuiltinKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBuiltIn => !string.IsNullOrWhiteSpace(BuiltinKey);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanDelete => !IsBuiltIn;

    [System.Text.Json.Serialization.JsonIgnore]
    public string KindLabel => IsBuiltIn ? "内置" : "自定义";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAvailable => string.IsNullOrWhiteSpace(Directory)
        ? IsBuiltIn
        : System.IO.Directory.Exists(Directory);
}
