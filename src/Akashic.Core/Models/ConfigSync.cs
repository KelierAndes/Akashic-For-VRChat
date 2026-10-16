namespace Akashic.Core.Models;

/// <summary>
/// What the 配置同步 page needs to know before it offers to upload or restore anything.
/// </summary>
public class SyncSnapshot
{
    public bool GhFound { get; init; }
    public string GhVersion { get; init; } = string.Empty;

    public bool SignedIn { get; init; }
    public string Login { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;

    /// <summary>The token gh holds must carry the gist scope or the upload is refused.</summary>
    public bool HasGistScope { get; init; }

    /// <summary>
    /// Deliberately no gist id and no gist URL in here. The id is enough to build the web address of
    /// the backup, and the encrypted blob names its owner — so the page shows only when the last
    /// upload happened. Locating the gist itself is the sidecar's job.
    /// </summary>
    public DateTime? UploadedAt { get; init; }

    /// <summary>
    /// The 启用 gist 同步 switch. GitHub sign-in stays useful without it — private external
    /// packages run on the same credential — so this only speaks for the sync itself.
    /// </summary>
    public bool Enabled { get; init; } = true;
    public int PayloadBytes { get; init; }

    /// <summary>What the next upload would carry, in the order the page lists it.</summary>
    public List<string> Included { get; init; } = [];

    /// <summary>Anything the page should say: a missing CLI, a missing scope, a bad restore.</summary>
    public string Note { get; init; } = string.Empty;

    public bool Ready => GhFound && SignedIn && HasGistScope;
}
