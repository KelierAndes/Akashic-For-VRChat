using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Reads and edits the signed-in VRChat account's own uploaded content through the site's own JSON
/// API. The session comes from the WebView2 login and is stored encrypted under the current Windows
/// account so a profile reset is not a sign-out.
/// </summary>
public interface IVRChatSyncService
{
    /// <summary>The vrchat.com cookies by name, for the hand-built <c>Cookie:</c> header.</summary>
    IReadOnlyDictionary<string, string>? SessionCookies { get; }

    /// <summary>The same cookies with their domains, for seeding a fresh WebView2 back.</summary>
    IReadOnlyList<StoredCookie>? SessionEntries { get; }

    /// <summary>Replaces the session with a fresh export; an empty set is ignored, not treated as a sign-out.</summary>
    void ImportSession(IReadOnlyList<StoredCookie> cookies);

    bool HasSession { get; }

    /// <summary>Where the WebView2 has to go to sign in.</summary>
    string LoginUrl { get; }

    /// <summary>Throws when the session is missing or dead, otherwise returns the account.</summary>
    Task<VRChatUser> GetSelfAsync(CancellationToken ct = default);

    /// <summary>Every avatar and world the account owns, public and private alike.</summary>
    Task<List<VRChatContentItem>> GetMyContentAsync(CancellationToken ct = default);

    /// <summary>
    /// The full record, which is where the fields the list omits live (a world's description,
    /// content settings and allowed video domains).
    /// </summary>
    Task<VRChatContentItem?> GetContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);

    /// <summary>Writes an edit back; untouched fields are echoed so the server does not reset them.</summary>
    Task<VRChatContentItem> UpdateContentAsync(VRChatContentEdit edit, CancellationToken ct = default);

    Task<bool> GetWorldPublishStatusAsync(string id, CancellationToken ct = default);

    Task PublishWorldAsync(string id, CancellationToken ct = default);

    Task UnpublishWorldAsync(string id, CancellationToken ct = default);

    /// <summary>Permanent: the site only flips the release status but the id stays reserved.</summary>
    Task DeleteContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default);

    /// <summary>Makes the avatar the account's current one, or its fallback.</summary>
    Task SelectAvatarAsync(string id, bool fallback, CancellationToken ct = default);

    Task EnqueueImpostorAsync(string id, string platform, CancellationToken ct = default);

    Task DeleteImpostorsAsync(string id, CancellationToken ct = default);

    Task<List<string>> GetAvatarStylesAsync(CancellationToken ct = default);

    /// <summary>Uploads a local picture as the site's cover-image endpoint does; returns its URL.</summary>
    Task<string> UploadImageAsync(string filePath, CancellationToken ct = default);

    /// <summary>The last list read successfully, so the page can show something before the network answers.</summary>
    VRChatContentCache LoadContentCache();

    Task SaveContentCacheAsync(IReadOnlyList<VRChatContentItem> items, CancellationToken ct = default);
}
