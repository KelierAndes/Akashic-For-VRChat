using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

public enum VRChatContentKind
{
    Avatar,
    World
}

public static class VRChatTagCatalog
{
    public static readonly (string Tag, string Label)[] ContentWarnings =
    [
        ("content_sex", "性暗示内容"),
        ("content_adult", "成人语言与主题"),
        ("content_violence", "暴力描写"),
        ("content_gore", "过度血腥"),
        ("content_horror", "极度恐怖")
    ];

    public const string AuthorTagPrefix = "author_tag_";

    public static readonly string[] ReadOnlyPrefixes = ["system_", "admin_", "feature_", "content_"];

    public static readonly int[] CapacityTiers = [6, 8, 12, 16, 20, 24, 32, 40];

    public static readonly (string Key, string Label)[] ContentSettings =
    [
        ("emoji", "表情"),
        ("stickers", "贴纸"),
        ("pedestals", "分享基座"),
        ("prints", "照片打印"),
        ("drones", "无人机")
    ];

    public static readonly (string Key, string Label)[] PropAbilities =
    [
        ("movement", "道具可改变玩家移动")
    ];

    public static bool IsAuthorTag(string tag) =>
        tag.StartsWith(AuthorTagPrefix, StringComparison.OrdinalIgnoreCase);

    public static string AuthorTagLabel(string tag) => IsAuthorTag(tag)
        ? tag[AuthorTagPrefix.Length..].Replace('_', ' ')
        : tag;

    public static string WarningLabel(string tag) =>
        ContentWarnings.FirstOrDefault(w => string.Equals(w.Tag, tag, StringComparison.OrdinalIgnoreCase)).Label ?? tag;
}

public sealed class LenientDateConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("O"));
    }
}

public class VRChatContentItem
{
    public VRChatContentKind Kind { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("authorId")]
    public string AuthorId { get; set; } = string.Empty;

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [JsonPropertyName("releaseStatus")]
    public string ReleaseStatus { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailImageUrl")]
    public string ThumbnailImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("capacity")]
    public int Capacity { get; set; }

    [JsonPropertyName("recommendedCapacity")]
    public int RecommendedCapacity { get; set; }

    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(LenientDateConverter))]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(LenientDateConverter))]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("publicationDate")]
    [JsonConverter(typeof(LenientDateConverter))]
    public DateTimeOffset? PublicationDate { get; set; }

    [JsonPropertyName("labsPublicationDate")]
    [JsonConverter(typeof(LenientDateConverter))]
    public DateTimeOffset? LabsPublicationDate { get; set; }

    [JsonPropertyName("performance")]
    public Dictionary<string, JsonElement> Performance { get; set; } = [];

    [JsonPropertyName("unityPackages")]
    public List<VRChatUnityPackage> UnityPackages { get; set; } = [];

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("unityPackageUrl")]
    public string UnityPackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("assetUrl")]
    public string AssetUrl { get; set; } = string.Empty;

    [JsonPropertyName("previewYoutubeId")]
    public string PreviewYoutubeId { get; set; } = string.Empty;

    [JsonPropertyName("urlList")]
    public List<string> UrlList { get; set; } = [];

    [JsonPropertyName("defaultContentSettings")]
    public Dictionary<string, JsonElement> DefaultContentSettings { get; set; } = [];

    [JsonPropertyName("disabledPropAbilities")]
    public List<string> DisabledPropAbilities { get; set; } = [];

    [JsonPropertyName("styles")]
    public VRChatAvatarStyles? Styles { get; set; }

    [JsonPropertyName("featured")]
    public bool Featured { get; set; }

    [JsonPropertyName("pendingUpload")]
    public bool PendingUpload { get; set; }

    [JsonPropertyName("searchable")]
    public bool? Searchable { get; set; }

    [JsonIgnore]
    public bool IsInLabs => Tags.Contains("system_labs", StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string KindDisplay => Kind == VRChatContentKind.Avatar ? "虚拟形象" : "世界";

    [JsonIgnore]
    public string BlueprintId => Id;

    [JsonIgnore]
    public string StatusDisplay => ReleaseStatus switch
    {
        "public" => "公开",
        "private" => "私有",
        "internal" => "内部",
        "all" => "全部",
        _ when string.IsNullOrEmpty(ReleaseStatus) => "未知",
        _ => ReleaseStatus
    };

    [JsonIgnore]
    public string UnityVersionDisplay =>
        UnityPackages.Where(p => !string.IsNullOrWhiteSpace(p.UnityVersion))
            .Select(p => p.UnityVersion)
            .LastOrDefault() ?? string.Empty;

    [JsonIgnore]
    public string UpdatedDisplay => UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "—";

    [JsonIgnore]
    public int MaxCapacity => Capacity > 0 ? Capacity : RecommendedCapacity * 2;

    [JsonIgnore]
    public string ExtraDisplay => Kind == VRChatContentKind.World
        ? (MaxCapacity > 0 ? $"容量 {MaxCapacity}" : "容量 —")
        : PerformanceDisplay;

    [JsonIgnore]
    public string PerformanceDisplay
    {
        get
        {
            foreach (var key in new[] { "standalonewindows", "android", "ios" })
            {
                if (Performance.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return $"评级 {text}";
                }
            }

            return "评级 —";
        }
    }
}

public class VRChatContentEdit
{
    public required VRChatContentItem Source { get; init; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public List<string>? Tags { get; set; }

    public string? ReleaseStatus { get; set; }

    public string? ImageUrl { get; set; }


    public int? MaxCapacity { get; set; }

    public string? PreviewYoutubeId { get; set; }

    public List<string>? UrlList { get; set; }

    public Dictionary<string, bool>? ContentSettings { get; set; }

    public List<string>? DisabledPropAbilities { get; set; }

    public bool? Publish { get; set; }


    public VRChatAvatarStyles? Styles { get; set; }
}

public class VRChatAvatarStyles
{
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public string? Secondary { get; set; }
}

public class VRChatUnityPackage
{
    [JsonPropertyName("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;
}

public class VRChatUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("currentAvatarDisplayName")]
    public string CurrentAvatarDisplayName { get; set; } = string.Empty;
}

public class VRChatContentCache
{
    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("items")]
    public List<VRChatContentItem> Items { get; set; } = [];

    public bool HasContent => Items.Count > 0;

    public string SavedAtDisplay => SavedAt == default ? "无缓存" : SavedAt.ToString("yyyy/MM/dd HH:mm");
}
