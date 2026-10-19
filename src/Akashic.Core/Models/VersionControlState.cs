namespace Akashic.Core.Models;

public class VcsTools
{
    public bool GitFound { get; init; }
    public string? GitPath { get; init; }
    public string? GitVersion { get; init; }

    public bool GitLfsFound { get; init; }
    public string? GitLfsVersion { get; init; }

    public bool CmFound { get; init; }
    public string? CmPath { get; init; }
    public string? CmVersion { get; init; }

    public bool GhFound { get; init; }
    public string? GhPath { get; init; }
    public string? GhVersion { get; init; }

    public bool UnityHubFound { get; init; }

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

public class VcsCommit
{
    public required string Id { get; init; }
    public required string Message { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;

    public string ShortId => Id.Length > 8 ? Id[..8] : Id;
}

public class ProjectVcsState
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }

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

    public int ChangedCount { get; init; }

    public bool LfsEnabled { get; init; }

    public int AheadCount { get; init; } = -1;

    public int BehindCount { get; init; } = -1;

    public List<VcsCommit> Commits { get; init; } = [];

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

public class VcsRunResult
{
    public bool Success { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string ProjectPath { get; init; } = string.Empty;

    public static VcsRunResult Fail(string action, string projectPath, string title, string output) =>
        new() { Success = false, Action = action, ProjectPath = projectPath, Title = title, Output = output };

    public static VcsRunResult Ok(string action, string projectPath, string title, string output) =>
        new() { Success = true, Action = action, ProjectPath = projectPath, Title = title, Output = output };
}
