namespace Akashic.Core.Models;

public class SyncSnapshot
{
    public bool GhFound { get; init; }
    public string GhVersion { get; init; } = string.Empty;

    public bool SignedIn { get; init; }
    public string Login { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;

    public bool HasGistScope { get; init; }

    public DateTime? UploadedAt { get; init; }

    public bool Enabled { get; init; } = true;
    public int PayloadBytes { get; init; }

    public List<string> Included { get; init; } = [];

    public string Note { get; init; } = string.Empty;

    public bool Ready => GhFound && SignedIn && HasGistScope;
}
