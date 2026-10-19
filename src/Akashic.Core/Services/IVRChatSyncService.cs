using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IVRChatSyncService
{
    IReadOnlyDictionary<string, string>? SessionCookies { get; }

    IReadOnlyList<StoredCookie>? SessionEntries { get; }

    void ImportSession(IReadOnlyList<StoredCookie> cookies);

    bool HasSession { get; }

    string LoginUrl { get; }

    Task<VRChatUser> GetSelfAsync(CancellationToken ct = default);

    Task<List<VRChatContentItem>> GetMyContentAsync(CancellationToken ct = default);

    Task<VRChatContentItem?> GetContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);

    Task<VRChatContentItem> UpdateContentAsync(VRChatContentEdit edit, CancellationToken ct = default);

    Task<bool> GetWorldPublishStatusAsync(string id, CancellationToken ct = default);

    Task PublishWorldAsync(string id, CancellationToken ct = default);

    Task UnpublishWorldAsync(string id, CancellationToken ct = default);

    Task DeleteContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);

    Task SelectAvatarAsync(string id, bool fallback, CancellationToken ct = default);

    Task EnqueueImpostorAsync(string id, string platform, CancellationToken ct = default);

    Task DeleteImpostorsAsync(string id, CancellationToken ct = default);

    Task<List<string>> GetAvatarStylesAsync(CancellationToken ct = default);

    Task<string> UploadImageAsync(string filePath, CancellationToken ct = default);

    VRChatContentCache LoadContentCache();

    Task SaveContentCacheAsync(IReadOnlyList<VRChatContentItem> items, CancellationToken ct = default);
}
