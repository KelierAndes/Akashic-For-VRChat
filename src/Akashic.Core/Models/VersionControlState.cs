namespace Akashic.Core.Models;

/// <summary>What the machine has installed for hosting project history.</summary>
public class VcsTools
{
    public bool GitFound { get; init; }
    public string? GitPath { get; init; }
    public string? GitVersion { get; init; }

    /// <summary>git ships without LFS; Unity projects need it for anything binary.</summary>
    public bool GitLfsFound { get; init; }
    public string? GitLfsVersion { get; init; }

    public bool CmFound { get; init; }
    public string? CmPath { get; init; }
    public string? CmVersion { get; init; }

    /// <summary>
    /// GitHub CLI. Every GitHub call — private repositories and the sync gist — goes through it,
    /// so it is a dependency of those two features rather than of hosting in general. It is
    /// deliberately left out of <see cref="Missing"/>: a project that never touches GitHub should
    /// not be nagged about it.
    /// </summary>
    public bool GhFound { get; init; }
    public string? GhPath { get; init; }
    public string? GhVersion { get; init; }

    /// <summary>Unity itself only says which VCS mode the project was opened in, if any.</summary>
    public bool UnityHubFound { get; init; }

    /// <summary>winget is the only installer this app drives; without it the download pages are shown.</summary>
    public bool InstallerFound { get; init; }
    public string? InstallerPath { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (GitFound) parts.Add($"git {GitVersion}");
            if (GitLfsFound) parts.Add("LFS 已装");
            if (CmFound) parts.Add($"UVCS {CmVersion}");
            return parts.Count == 0 ? "未检测到 git 或 Unity Version Control" : string.Join(" · ", parts);
        }
    }

    /// <summary>What still has to be installed before every hosting feature works.</summary>
    public IReadOnlyList<string> Missing
    {
        get
        {
            var missing = new List<string>();
            if (!GitFound) missing.Add("git");
            if (!GitLfsFound) missing.Add("Git LFS");
            if (!CmFound) missing.Add("Unity Version Control");
            return missing;
        }
    }
}

/// <summary>One revision shown in the history list.</summary>
public class VcsCommit
{
    public required string Id { get; init; }
    public required string Message { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;

    public string ShortId => Id.Length > 8 ? Id[..8] : Id;
}

/// <summary>
/// How a single project is currently hosted, read from the markers on disk so that
/// listing ten projects never has to wait on ten external processes.
/// </summary>
public class ProjectVcsState
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }

    /// <summary>git, uvcs, both, or empty when the folder is unmanaged.</summary>
    public string Kind { get; init; } = string.Empty;

    public string KindDisplay => Kind switch
    {
        "git" => "Git",
        "uvcs" => "Unity Version Control",
        "both" => "Git + UVCS",
        _ => "未托管"
    };

    public string Branch { get; init; } = string.Empty;
    public string Remote { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;

    /// <summary>Files git or UVCS reports as changed; -1 when the count could not be taken.</summary>
    public int ChangedCount { get; init; }

    /// <summary>True when the repository has the LFS filters installed.</summary>
    public bool LfsEnabled { get; init; }

    /// <summary>Commits ahead of the remote, -1 when unknown.</summary>
    public int AheadCount { get; init; } = -1;

    /// <summary>-1 when unknown: commits behind the remote, i.e. a pull is pending.</summary>
    public int BehindCount { get; init; } = -1;

    public List<VcsCommit> Commits { get; init; } = [];

    /// <summary>Anything the reader wants to say: a parse failure, a partial marker, an SSO hint.</summary>
    public string Note { get; init; } = string.Empty;

    public bool Managed => Kind.Length > 0;

    public string StatusDisplay => Kind switch
    {
        "" => "未托管",
        _ when ChangedCount < 0 => string.IsNullOrEmpty(Branch) ? "已托管" : Branch,
        _ => $"{(string.IsNullOrEmpty(Branch) ? "已托管" : Branch)} · {ChangedCount} 项改动" +
            (AheadCount > 0 ? $" · 待推送 {AheadCount}" : string.Empty) +
            (BehindCount > 0 ? $" · 待拉取 {BehindCount}" : string.Empty)
    } + (LfsEnabled ? " · LFS" : string.Empty);
}

/// <summary>The outcome of one operation, with the tool's own output kept verbatim.</summary>
public class VcsRunResult
{
    public bool Success { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;

    /// <summary>What the caller asked for; the host echoes it back so the page can refresh itself.</summary>
    public string Action { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public static VcsRunResult Fail(string action, string projectPath, string title, string output) =>
        new() { Success = false, Action = action, ProjectPath = projectPath, Title = title, Output = output };

    public static VcsRunResult Ok(string action, string projectPath, string title, string output) =>
        new() { Success = true, Action = action, ProjectPath = projectPath, Title = title, Output = output };
}
