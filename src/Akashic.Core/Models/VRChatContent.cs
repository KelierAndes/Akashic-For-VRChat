using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>Which kind of uploaded content a row stands for.</summary>
public enum VRChatContentKind
{
    Avatar,
    World
}

/// <summary>
/// The tag vocabulary and picker options the official edit pages use, so the app and the site agree
/// on what a switch actually writes.
/// </summary>
public static class VRChatTagCatalog
{
    /// <summary>The site's Content Warnings switches, by their real tag values.</summary>
    public static readonly (string Tag, string Label)[] ContentWarnings =
    [
        ("content_sex", "性暗示内容"),
        ("content_adult", "成人语言与主题"),
        ("content_violence", "暴力描写"),
        ("content_gore", "过度血腥"),
        ("content_horror", "极度恐怖")
    ];

    /// <summary>Author-selectable tags, i.e. the site's Tags field (stored with this prefix).</summary>
    public const string AuthorTagPrefix = "author_tag_";

    /// <summary>Tag families an author cannot edit; they are carried through untouched.</summary>
    public static readonly string[] ReadOnlyPrefixes = ["system_", "admin_", "feature_", "content_"];

    /// <summary>The site's Maximum Capacity dropdown; the API stores half of it as the recommended count.</summary>
    public static readonly int[] CapacityTiers = [6, 8, 12, 16, 20, 24, 32, 40];

    /// <summary>
    /// The site's Default Content Settings switches, by the internal names its form uses. Anything
    /// the server reports beyond these is carried through untouched.
    /// </summary>
    public static readonly (string Key, string Label)[] ContentSettings =
    [
        ("emoji", "表情"),
        ("stickers", "贴纸"),
        ("pedestals", "分享基座"),
        ("prints", "照片打印"),
        ("drones", "无人机")
    ];

    /// <summary>The one prop ability the site exposes as a switch.</summary>
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

/// <summary>
/// VRChat's date fields are unreliable: an unpublished world reports an empty string rather than
/// null, which a plain DateTimeOffset? refuses to read and takes the whole list down with it.
/// </summary>
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

/// <summary>
/// One piece of content the signed-in account owns. VRChat's content id (avtr_… / wrld_…) is the
/// same value the Unity SDK shows as the Blueprint ID, so the page labels it that way.
/// </summary>
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

    /// <summary>Full-size artwork; the API hands back an absolute api.vrchat.cloud URL.</summary>
    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>The 256px thumbnail the list shows.</summary>
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

    /// <summary>
    /// Performance ratings keyed by platform, e.g. standalonewindows = "Very Poor". The API nests
    /// nothing here, so it is read as-is rather than through a rating object.
    /// </summary>
    [JsonPropertyName("performance")]
    public Dictionary<string, JsonElement> Performance { get; set; } = [];

    /// <summary>Raw unityPackages array; only the newest entry's version is interesting here.</summary>
    [JsonPropertyName("unityPackages")]
    public List<VRChatUnityPackage> UnityPackages { get; set; } = [];

    /// <summary>Avatar records carry a version counter the update call expects back unchanged.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("unityPackageUrl")]
    public string UnityPackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("assetUrl")]
    public string AssetUrl { get; set; } = string.Empty;

    /// <summary>YouTube video id behind the world's preview player.</summary>
    [JsonPropertyName("previewYoutubeId")]
    public string PreviewYoutubeId { get; set; } = string.Empty;

    /// <summary>Hosts the world's video players are allowed to stream from.</summary>
    [JsonPropertyName("urlList")]
    public List<string> UrlList { get; set; } = [];

    /// <summary>
    /// The site's "Default Content Settings" switches. Kept as raw JSON so keys this app does not
    /// model survive an edit round-trip untouched.
    /// </summary>
    [JsonPropertyName("defaultContentSettings")]
    public Dictionary<string, JsonElement> DefaultContentSettings { get; set; } = [];

    /// <summary>Prop abilities switched off in the site's props section.</summary>
    [JsonPropertyName("disabledPropAbilities")]
    public List<string> DisabledPropAbilities { get; set; } = [];

    /// <summary>
    /// Avatar styles for the site's carousel, as {primary, secondary}. Read as an object: the list
    /// hands back an object, so modelling it as an array breaks deserialising every avatar row.
    /// </summary>
    [JsonPropertyName("styles")]
    public VRChatAvatarStyles? Styles { get; set; }

    [JsonPropertyName("featured")]
    public bool Featured { get; set; }

    /// <summary>Set while the SDK has an upload staged for this record.</summary>
    [JsonPropertyName("pendingUpload")]
    public bool PendingUpload { get; set; }

    [JsonPropertyName("searchable")]
    public bool? Searchable { get; set; }

    /// <summary>True while the account's own copy is the world's author-controlled record.</summary>
    [JsonIgnore]
    public bool IsInLabs => Tags.Contains("system_labs", StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string KindDisplay => Kind == VRChatContentKind.Avatar ? "虚拟形象" : "世界";

    /// <summary>The SDK's Blueprint ID — the same id the API keys the content by.</summary>
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

    /// <summary>The site shows the hard limit; the API's capacity field is the recommended count.</summary>
    [JsonIgnore]
    public int MaxCapacity => Capacity > 0 ? Capacity : RecommendedCapacity * 2;

    [JsonIgnore]
    public string ExtraDisplay => Kind == VRChatContentKind.World
        ? (MaxCapacity > 0 ? $"容量 {MaxCapacity}" : "容量 —")
        : PerformanceDisplay;

    /// <summary>The PC rating the site shows, falling back to the mobile one.</summary>
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

/// <summary>
/// The editable half of a record. The two kinds share the text fields but their option sets differ,
/// exactly as the official edit pages do: worlds carry capacity, content settings and publishing,
/// avatars carry impostors and style slots. Nulls mean "leave the server's value alone" — the
/// update endpoint resets anything it is not given, so untouched fields are echoed back.
/// </summary>
public class VRChatContentEdit
{
    public required VRChatContentItem Source { get; init; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Full tag list, prefixes included; system tags are carried over untouched.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>public / private, matching the site's visibility switch.</summary>
    public string? ReleaseStatus { get; set; }

    /// <summary>Already-uploaded cover file URL, from the image upload endpoint.</summary>
    public string? ImageUrl { get; set; }

    // ── World only ────────────────────────────────────────────────────────────────────────

    /// <summary>The site's Maximum Capacity; the API stores half of it as the recommended count.</summary>
    public int? MaxCapacity { get; set; }

    /// <summary>YouTube video id behind the world's preview player.</summary>
    public string? PreviewYoutubeId { get; set; }

    /// <summary>Hosts the world's video players may stream from.</summary>
    public List<string>? UrlList { get; set; }

    /// <summary>The site's Default Content Settings switches, keyed by their internal names.</summary>
    public Dictionary<string, bool>? ContentSettings { get; set; }

    /// <summary>Prop abilities turned off, e.g. "movement".</summary>
    public List<string>? DisabledPropAbilities { get; set; }

    /// <summary>Ask the server to publish (true) or unpublish (false) the world.</summary>
    public bool? Publish { get; set; }

    // ── Avatar only ───────────────────────────────────────────────────────────────────────

    /// <summary>Style slots shown in the site's avatar carousel.</summary>
    public VRChatAvatarStyles? Styles { get; set; }
}

/// <summary>The two style slots an avatar can occupy in the site's carousel.</summary>
public class VRChatAvatarStyles
{
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public string? Secondary { get; set; }
}

/// <summary>One entry of an item's unityPackages array (per-Unity-version builds).</summary>
public class VRChatUnityPackage
{
    [JsonPropertyName("unityVersion")]
    public string UnityVersion { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;
}

/// <summary>The signed-in VRChat account, as far as this page needs it.</summary>
public class VRChatUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("currentAvatarDisplayName")]
    public string CurrentAvatarDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// The last content list this machine read successfully. Only the list is kept — the session that
/// made it possible stays in memory and is never written out.
/// </summary>
public class VRChatContentCache
{
    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("items")]
    public List<VRChatContentItem> Items { get; set; } = [];

    public bool HasContent => Items.Count > 0;

    public string SavedAtDisplay => SavedAt == default ? "无缓存" : SavedAt.ToString("yyyy/MM/dd HH:mm");
}
