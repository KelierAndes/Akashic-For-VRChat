namespace Akashic.Core.Models;

/// <summary>
/// A saved project skeleton new projects are created from: the package manifests and editor
/// settings of a project the user liked, kept as a folder of its own so deleting the source project
/// never takes the template with it.
/// </summary>
public class ProjectTemplate
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>世界 / 虚拟形象 / 自定义, mirroring what the VRChat SDK template a project uses.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "自定义";

    [System.Text.Json.Serialization.JsonPropertyName("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>The folder holding the snapshot; absolute so the list survives any later move.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    /// <summary>The project the snapshot was taken from, shown so the user recognises it.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string CreatedAtDisplay => IsBuiltIn ? "随 Creator Companion" : CreatedAt.ToString("yyyy/MM/dd HH:mm");

    /// <summary>Empty notes still need a line in the table, or the row reads as broken.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string NotesDisplay => string.IsNullOrWhiteSpace(Notes) ? "（无说明）" : Notes;

    /// <summary>The folder name of the project this was taken from.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceDisplay => IsBuiltIn
        ? "VRChat Creator Companion 自带"
        : string.IsNullOrWhiteSpace(SourcePath)
            ? "未知来源"
            : System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(SourcePath));

    /// <summary>
    /// Set on the templates the app ships with, which have no snapshot folder and cannot be deleted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuiltinKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBuiltIn => !string.IsNullOrWhiteSpace(BuiltinKey);

    /// <summary>Only a template this app saved can be deleted; the official ones come with VCC.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanDelete => !IsBuiltIn;

    /// <summary>The folder this project starts from; empty for a built-in, which writes its own.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string KindLabel => IsBuiltIn ? "内置" : "自定义";

    /// <summary>
    /// A template with a folder is only usable while that folder is there — which for an official one
    /// means VCC still ships it. The compiled-in fallback has no folder and is always available.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAvailable => string.IsNullOrWhiteSpace(Directory)
        ? IsBuiltIn
        : System.IO.Directory.Exists(Directory);
}
