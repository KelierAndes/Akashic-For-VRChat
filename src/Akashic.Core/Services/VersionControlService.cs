using System.Diagnostics;
using System.Text;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IVersionControlService
{
    /// <summary>Which hosting tools this machine has, with their versions.</summary>
    Task<VcsTools> DetectToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Drops the memo so the next probe sees a tool that was installed, uninstalled or updated
    /// outside this app. Without it the settings page's 重新检测 answers from cache forever.
    /// </summary>
    void ForgetTools();

    /// <summary>
    /// Reads how one project is hosted. Cheap by default: the markers on disk are parsed and no
    /// external tool is asked. <paramref name="deep"/> adds the changed-file counts and history,
    /// which does have to run git or cm.
    /// </summary>
    Task<ProjectVcsState> ReadStateAsync(string projectPath, bool deep = false, CancellationToken ct = default);

    Task<VcsRunResult> InitializeAsync(string projectPath, string kind, CancellationToken ct = default);

    Task<VcsRunResult> WriteGitignoreAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Installs a missing tool with winget, then re-detects.</summary>
    Task<VcsRunResult> InstallAsync(string tool, CancellationToken ct = default);

    /// <summary>Turns LFS on for one repository only, and writes the Unity attribute file.</summary>
    Task<VcsRunResult> EnableLfsAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Takes the LFS filters back out of this repository only.</summary>
    Task<VcsRunResult> DisableLfsAsync(string projectPath, CancellationToken ct = default);

    Task<VcsRunResult> SetRemoteAsync(string projectPath, string url, CancellationToken ct = default);

    Task<VcsRunResult> SubmitAsync(string projectPath, string message, bool pushAfter, CancellationToken ct = default);

    Task<VcsRunResult> UpdateAsync(string projectPath, CancellationToken ct = default);

    Task<VcsRunResult> PushAsync(string projectPath, CancellationToken ct = default);
}

/// <summary>
/// Talks to git and to Unity Version Control (cm) for the projects that are hosted, and hosts the
/// ones that are not. Everything runs as an argument array against the executable, never through a
/// shell, so a folder or a commit message with spaces or quotes cannot turn into a command.
/// </summary>
public class VersionControlService : IVersionControlService
{
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);

    private Task<VcsTools>? _toolsTask;

    // ── Tool discovery ────────────────────────────────────────────────────────────────────────

    public Task<VcsTools> DetectToolsAsync(CancellationToken ct = default)
    {
        // A cancelled probe must not poison every later read, so only a finished one is kept.
        if (_toolsTask is { IsCompletedSuccessfully: true }) return _toolsTask;
        return _toolsTask = ProbeAsync(ct);
    }

    public void ForgetTools() => _toolsTask = null;

    private static async Task<VcsTools> ProbeAsync(CancellationToken ct)
    {
        var gitPath = FindOnPathOr("git.exe",
            [
                @"C:\Program Files\Git\cmd\git.exe",
                @"C:\Program Files (x86)\Git\cmd\git.exe",
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\Git\cmd\git.exe")
            ]);

        var cmPath = FindOnPathOr("cm.exe",
            [
                @"C:\Program Files\PlasticSCM5\client\cm.exe",
                @"C:\Program Files\PlasticSCM\client\cm.exe",
                @"C:\Program Files\Unity Version Control\cm.exe",
                @"C:\Program Files (x86)\PlasticSCM5\client\cm.exe"
            ]);

        var gitVersion = await FirstLineAsync(gitPath, ["--version"], ct);
        // git-lfs is a separate binary that git finds next to itself, so the only
        // portable question to ask git is whether the subcommand answers.
        var lfsVersion = gitPath == null
            ? null
            : await FirstLineAsync(gitPath, ["lfs", "version"], ct);
        var cmVersion = await FirstLineAsync(cmPath, ["version"], ct);

        var ghPath = FindOnPathOr("gh.exe", GitHubCli.InstallCandidates);
        var ghVersion = await FirstLineAsync(ghPath, ["--version"], ct);

        var wingetPath = FindOnPathOr("winget.exe",
        [
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe"),
            @"C:\Windows\System32\winget.exe"
        ]);

        var tools = new VcsTools
        {
            GitFound = gitPath != null,
            GitPath = gitPath,
            GitVersion = ShortVersion(gitVersion),
            GitLfsFound = !string.IsNullOrWhiteSpace(lfsVersion),
            GitLfsVersion = ShortVersion(lfsVersion),
            CmFound = cmPath != null,
            CmPath = cmPath,
            CmVersion = ShortVersion(cmVersion),
            GhFound = ghPath != null,
            GhPath = ghPath,
            GhVersion = ShortVersion(ghVersion),
            InstallerFound = wingetPath != null,
            InstallerPath = wingetPath
        };
        return tools;
    }

    private static string? FindOnPathOr(string fileName, string[] extraCandidates)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }

        foreach (var candidate in extraCandidates)
            if (File.Exists(candidate)) return candidate;

        return null;
    }

    private static async Task<string?> FirstLineAsync(string? exe, string[] args, CancellationToken ct)
    {
        if (exe == null) return null;

        // git and gh always answer in UTF-8; cm writes in the console codepage, so it has to
        // keep the system default.
        var utf8 = exe.EndsWith("git.exe", StringComparison.OrdinalIgnoreCase) ||
                   exe.EndsWith("gh.exe", StringComparison.OrdinalIgnoreCase);

        var (code, output) = await RunProcAsync(exe, Environment.CurrentDirectory, args, QuickTimeout,
            utf8 ? Encoding.UTF8 : null, ct);
        return code == 0 ? output.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) : null;
    }

    /// <summary>
    /// Each tool prints its version differently: `git version 2.42.0.windows.1`,
    /// `git-lfs/3.8.0 (GitHub; windows amd64; go 1.25.9; git 77deabdf)` and a bare
    /// `11.0.16.10275` from cm. Taking the last word, as this once did, reads the build
    /// hash out of the LFS line and shows that as the version.
    /// </summary>
    private static string? ShortVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();

        var slash = text.IndexOf('/');
        if (slash > 0)
        {
            var end = text.IndexOf(' ', slash);
            return end < 0 ? text[(slash + 1)..] : text[(slash + 1)..end];
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+(\.\d+)+");
        return match.Success ? match.Value : text;
    }

    // ── State ─────────────────────────────────────────────────────────────────────────────────

    public async Task<ProjectVcsState> ReadStateAsync(
        string projectPath, bool deep = false, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(projectPath);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!Directory.Exists(full))
            return new ProjectVcsState
            {
                ProjectPath = full,
                ProjectName = name,
                Note = "目录不存在。"
            };

        var git = ReadGit(full);
        var uvcs = ReadUvcs(full);

        var kind = (git.Found, uvcs.Found) switch
        {
            (true, true) => "both",
            (true, false) => "git",
            (false, true) => "uvcs",
            _ => string.Empty
        };

        var state = new ProjectVcsState
        {
            ProjectPath = full,
            ProjectName = name,
            Kind = kind,
            Branch = git.Branch ?? uvcs.Branch ?? string.Empty,
            Remote = git.Remote ?? uvcs.Remote ?? string.Empty,
            Provider = git.Found
                ? ProviderOf(git.Remote)
                : uvcs.Found
                    ? uvcs.Provider ?? string.Empty
                    : string.Empty,
            ChangedCount = -1,
            LfsEnabled = git.LfsEnabled,
            Note = git.Note + uvcs.Note
        };

        if (!deep || kind.Length == 0) return state;

        var tools = await DetectToolsAsync(ct);

        if (kind is "git" or "both" && tools.GitPath != null)
        {
            var (code, output) = await RunProcAsync(tools.GitPath, full,
                ["status", "--porcelain=v2", "--branch"], QuickTimeout, Encoding.UTF8, ct);

            if (code == 0)
            {
                ParseGitStatus(output, out var changed, out var ahead, out var behind, out var upstream);
                state = Clone(state, changed, ahead, behind,
                    string.IsNullOrEmpty(state.Branch) ? upstream : state.Branch);
            }
            else
            {
                state = Clone(state, state.ChangedCount, state.AheadCount, state.BehindCount, state.Branch,
                    $"git status: {Trim(output)}");
            }

            var (logCode, logText) = await RunProcAsync(tools.GitPath, full,
                ["log", "--max-count=8", "--pretty=format:%h%x1f%an%x1f%ad%x1f%s", "--date=format:%Y/%m/%d %H:%M"],
                QuickTimeout, Encoding.UTF8, ct);

            var commits = logCode == 0
                ? logText.Split('\n')
                    .Select(line => line.Split('\u001f'))
                    .Where(p => p.Length >= 4)
                    .Select(p => new VcsCommit
                    {
                        Id = p[0], Author = p[1], Date = p[2], Message = p[3]
                    })
                    .ToList()
                : [];

            state = WithCommits(state, commits);
        }
        else if (kind == "uvcs" && tools.CmPath != null)
        {
            var (code, output) = await RunProcAsync(tools.CmPath, full,
                ["status", "--short", "--machinereadable"], QuickTimeout, null, ct);

            var lines = output.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
            state = Clone(state, code == 0 ? lines : -1, state.AheadCount, state.BehindCount, state.Branch,
                code == 0
                    ? state.Note
                    : $"cm status: {Trim(output)}（改动数没读出来，提交与更新仍可尝试）");
        }

        return state;
    }

    private static ProjectVcsState Clone(ProjectVcsState s, int changed, int ahead, int behind,
        string branch, string? note = null) => new()
    {
        ProjectPath = s.ProjectPath,
        ProjectName = s.ProjectName,
        Kind = s.Kind,
        Branch = branch,
        Remote = s.Remote,
        Provider = s.Provider,
        ChangedCount = changed,
        AheadCount = ahead,
        BehindCount = behind,
        LfsEnabled = s.LfsEnabled,
        Commits = s.Commits,
        Note = note ?? s.Note
    };

    private static ProjectVcsState WithCommits(ProjectVcsState s, List<VcsCommit> commits) => new()
    {
        ProjectPath = s.ProjectPath,
        ProjectName = s.ProjectName,
        Kind = s.Kind,
        Branch = s.Branch,
        Remote = s.Remote,
        Provider = s.Provider,
        ChangedCount = s.ChangedCount,
        AheadCount = s.AheadCount,
        BehindCount = s.BehindCount,
        LfsEnabled = s.LfsEnabled,
        Commits = commits,
        Note = s.Note
    };

    private static void ParseGitStatus(string output, out int changed, out int ahead, out int behind,
        out string upstream)
    {
        changed = 0;
        ahead = -1;
        behind = -1;
        upstream = string.Empty;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.upstream "))
            {
                upstream = line[18..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.ab "))
            {
                // "# branch.ab +2 -1": two to push, one to pull.
                var parts = line[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.StartsWith('+') && int.TryParse(part[1..], out var a)) ahead = a;
                    if (part.StartsWith('-') && int.TryParse(part[1..], out var b)) behind = b;
                }

                continue;
            }

            if (line.Length > 0 && !line.StartsWith('#')) changed++;
        }
    }

    private static string ProviderOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "本地仓库";
        var text = url.ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("github.com") => "GitHub",
            _ when text.Contains("gitlab") => "GitLab",
            _ when text.Contains("bitbucket") => "Bitbucket",
            _ when text.Contains("dev.azure.com") => "Azure DevOps",
            _ when text.Contains("unity") || text.Contains("plasticscm") || text.Contains("@cloud") => "Unity 云端",
            _ when text.StartsWith("file:") || Path.IsPathRooted(text) => "本地仓库",
            _ => "自建 / 其他"
        };
    }

    // ── Markers on disk ───────────────────────────────────────────────────────────────────────

    private sealed record MarkerRead(
        bool Found, string? Branch = null, string? Remote = null, string? Provider = null,
        string Note = "", bool LfsEnabled = false);

    private static MarkerRead ReadGit(string projectPath)
    {
        var gitDir = ResolveGitDir(projectPath);
        if (gitDir == null) return new MarkerRead(false);

        string? branch = null;
        var head = Path.Combine(gitDir, "HEAD");
        if (File.Exists(head))
        {
            var text = ReadAll(head);
            if (text.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                branch = text[(text.LastIndexOf('/') + 1)..].Trim();
            else
                branch = "游离 HEAD";
        }

        string? remote = null;
        var lfs = false;
        var configPath = GitConfigPath(gitDir);
        if (configPath != null)
        {
            var config = ReadAll(configPath);
            remote = ReadOriginUrl(config);
            // Reading the section is enough to know LFS is on, and it costs nothing:
            // the alternative is spawning `git config` once per project just to list them.
            lfs = config.Contains("[filter \"lfs\"]", StringComparison.OrdinalIgnoreCase);
        }

        // A .git folder with no config at all is a repo that has never committed; saying
        // "已托管" for it would be a lie, so the branch stays empty and the note explains.
        var note = branch == null ? "（.git 存在但读不出分支）" : string.Empty;
        return new MarkerRead(true, branch, remote, remote == null ? "本地仓库" : ProviderOf(remote), note, lfs);
    }

    private static string? ResolveGitDir(string projectPath)
    {
        var direct = Path.Combine(projectPath, ".git");
        if (Directory.Exists(direct)) return direct;
        if (!File.Exists(direct)) return null;

        // Worktrees and submodules leave a .git file that points at the real directory.
        var text = ReadAll(direct);
        const string marker = "gitdir:";
        if (!text.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return null;

        var target = text[marker.Length..].Trim();
        if (!Path.IsPathRooted(target)) target = Path.Combine(projectPath, target);
        return Directory.Exists(target) ? Path.GetFullPath(target) : null;
    }

    private static string? GitConfigPath(string gitDir)
    {
        var own = Path.Combine(gitDir, "config");
        if (File.Exists(own)) return own;

        var common = Path.Combine(gitDir, "commondir");
        if (!File.Exists(common)) return null;

        var target = ReadAll(common).Trim();
        if (!Path.IsPathRooted(target)) target = Path.Combine(gitDir, target);
        var config = Path.GetFullPath(Path.Combine(target, "config"));
        return File.Exists(config) ? config : null;
    }

    /// <summary>Picks the origin URL out of git's ini-ish config without needing git itself.</summary>
    private static string? ReadOriginUrl(string config)
    {
        string? section = null;
        foreach (var raw in config.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (section != "remote \"origin\"" || !line.Contains('=')) continue;

            var value = line[(line.IndexOf('=') + 1)..].Trim();
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length > 1)
                value = value[1..^1];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static MarkerRead ReadUvcs(string projectPath)
    {
        var plasticDir = Path.Combine(projectPath, ".plastic");
        if (!Directory.Exists(plasticDir)) return new MarkerRead(false);

        string? branch = null;
        string? remote = null;
        string? provider = null;

        // Unity writes the workspace selector next to the workspace tree; it names the repository
        // and the branch or changeset the folder is currently loaded at.
        var selector = Path.Combine(plasticDir, "plastic.selector");
        if (File.Exists(selector))
        {
            var text = ReadAll(selector);
            remote = Between(text, "repository", '"');
            branch = Between(text, "co", '"') ?? Between(text, "br", '"');
            if (branch != null) branch = branch.TrimStart('/');
            provider = remote != null && remote.EndsWith("@cloud", StringComparison.OrdinalIgnoreCase)
                ? "Unity 云端"
                : remote != null ? "自建 / 其他" : "本地仓库";
        }

        var workspace = Path.Combine(plasticDir, "plastic.workspace");
        if (File.Exists(workspace))
        {
            var first = ReadAll(workspace).Split('\n').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(remote))
                remote = $"{remote}（工作区 {first}）";
        }

        return new MarkerRead(true, branch, remote, provider,
            remote == null ? "（.plastic 存在但读不出仓库）" : string.Empty);
    }

    private static string? Between(string text, string key, char quote)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(key + " ", StringComparison.Ordinal)) continue;
            var start = line.IndexOf(quote);
            var end = line.IndexOf(quote, start + 1);
            if (start < 0 || end <= start) continue;
            return line[(start + 1)..end];
        }

        return null;
    }

    private static string ReadAll(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    // ── Operations ────────────────────────────────────────────────────────────────────────────

    public async Task<VcsRunResult> InitializeAsync(string projectPath, string kind, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        if (!Directory.Exists(path))
            return VcsRunResult.Fail("init", path, "目录不存在", path);

        var tools = await DetectToolsAsync(ct);

        if (kind == "uvcs")
        {
            if (tools.CmPath == null)
                return VcsRunResult.Fail("init", path, "未安装 Unity Version Control",
                    "没有在本机找到 cm.exe。可以在 Unity 编辑器的 Project Settings → Version Control 里创建 workspace。");

            var name = SafeName(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)));
            var log = new StringBuilder();
            var (code, output) = await RunProcAsync(tools.CmPath, Path.GetDirectoryName(path) ?? path,
                ["workspace", "create", name, path], NetworkTimeout, null, ct);
            log.AppendLine($"cm workspace create {name} {path}\n{output}");
            if (code != 0)
                return VcsRunResult.Fail("init", path, "创建 UVCS workspace 失败", log.ToString());

            var ignore = await WriteIgnoreFileAsync(path, ".plasticignore", PlasticIgnore);
            log.Append(ignore);
            return VcsRunResult.Ok("init", path, $"已创建 UVCS workspace「{name}」", log.ToString());
        }

        if (tools.GitPath == null)
            return VcsRunResult.Fail("init", path, "未安装 git",
                "没有在本机找到 git.exe。可以先安装 Git for Windows，或在 Unity 里改用 Version Control。");

        var logText = new StringBuilder();
        var (initCode, initOut) = await RunProcAsync(tools.GitPath, path, ["init", "-b", "main"], QuickTimeout,
            Encoding.UTF8, ct);
        logText.AppendLine($"git init\n{Trim(initOut)}");
        if (initCode != 0)
        {
            // Older git has no -b; the branch is then renamed instead.
            var (fallbackCode, fallbackOut) = await RunProcAsync(tools.GitPath, path, ["init"],
                QuickTimeout, Encoding.UTF8, ct);
            logText.AppendLine($"git init\n{Trim(fallbackOut)}");
            if (fallbackCode != 0)
                return VcsRunResult.Fail("init", path, "git init 失败", logText.ToString());
        }

        logText.Append(await WriteIgnoreFileAsync(path, ".gitignore", UnityGitignore));

        // Unity projects are mostly binary art, so a fresh repository starts with LFS on
        // whenever the machine has it; without it the first push of a few gigabytes fails.
        if (tools.GitLfsFound)
        {
            var (lfsCode, lfsOut) = await RunProcAsync(tools.GitPath, path,
                ["lfs", "install", "--local"], QuickTimeout, Encoding.UTF8, ct);
            logText.AppendLine($"\ngit lfs install --local\n{Trim(lfsOut)}");
            if (lfsCode == 0)
                logText.Append(await WriteIgnoreFileAsync(path, ".gitattributes", UnityGitattributes));
        }
        else
        {
            logText.AppendLine("\n未检测到 Git LFS，二进制资源会直接进仓库；装好后可在这里点「启用 LFS」。");
        }

        var (addCode, addOut) = await RunProcAsync(tools.GitPath, path, ["add", "-A"],
            TimeSpan.FromMinutes(10), Encoding.UTF8, ct);
        logText.AppendLine($"\ngit add -A\n{Trim(addOut)}");
        if (addCode != 0) return VcsRunResult.Fail("init", path, "git add 失败", logText.ToString());

        var (commitCode, commitOut) = await RunProcAsync(tools.GitPath, path,
            ["commit", "--message=通过 Akashic 接入版本托管"], NetworkTimeout, Encoding.UTF8, ct);
        logText.AppendLine($"\ngit commit\n{Trim(commitOut)}");

        return commitCode == 0
            ? VcsRunResult.Ok("init", path, "已接入 git 并完成首次提交", logText.ToString())
            : VcsRunResult.Fail("init", path, "已建立仓库，但首次提交失败", logText.ToString());
    }

    public async Task<VcsRunResult> WriteGitignoreAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        if (!Directory.Exists(path))
            return VcsRunResult.Fail("gitignore", path, "目录不存在", path);

        var state = await ReadStateAsync(path, ct: ct);
        if (!state.Managed)
            return VcsRunResult.Fail("gitignore", path, "工程尚未接入版本托管",
                "先接入 git 或 Unity Version Control，再接 ignore 文件。");

        var log = new StringBuilder();
        if (state.Kind is "git" or "both") log.Append(await WriteIgnoreFileAsync(path, ".gitignore", UnityGitignore));
        if (state.Kind is "uvcs" or "both") log.Append(await WriteIgnoreFileAsync(path, ".plasticignore", PlasticIgnore));

        return VcsRunResult.Ok("gitignore", path, "忽略规则已就绪", log.ToString().Trim());
    }

    /// <summary>
    /// The three things the hosting features need. Git for Windows ships LFS with it, but
    /// LFS is listed separately because a hand-rolled git install often lacks it.
    /// </summary>
    private static readonly Dictionary<string, (string Id, string Name, string Url)> Installable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["git"] = ("Git.Git", "Git for Windows", "https://git-scm.com/download/win"),
            ["lfs"] = ("GitHub.GitLFS", "Git LFS", "https://git-lfs.com/"),
            ["uvcs"] = ("Codice.PlasticSCM.CloudEdition", "Unity Version Control",
                "https://unity.com/products/version-control"),
            ["gh"] = ("GitHub.cli", "GitHub CLI", "https://cli.github.com/"),
        };

    public async Task<VcsRunResult> InstallAsync(string tool, CancellationToken ct = default)
    {
        const string action = "install";
        if (!Installable.TryGetValue(tool ?? string.Empty, out var package))
            return VcsRunResult.Fail(action, string.Empty, "未知的安装目标", tool ?? string.Empty);

        var before = await DetectToolsAsync(ct);
        if (!before.InstallerFound || before.InstallerPath == null)
            return VcsRunResult.Fail(action, string.Empty, $"本机没有 winget，无法自动安装 {package.Name}",
                $"请手动下载安装 {package.Name}：\n{package.Url}\n装好后回到这里点「重新检测」。");

        var (code, output) = await RunProcAsync(before.InstallerPath, Path.GetTempPath(),
        [
            "install", "--id", package.Id, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements"
        ], TimeSpan.FromMinutes(15), null, ct);

        // A binary installed moments ago is not on this process's PATH, so the probe has to
        // run again against the fixed install folders; dropping the cache is what makes it.
        _toolsTask = null;
        var after = await DetectToolsAsync(ct);

        var found = package.Id switch
        {
            "Git.Git" => after.GitFound,
            "GitHub.GitLFS" => after.GitLfsFound,
            "GitHub.cli" => after.GhFound,
            _ => after.CmFound
        };

        var text = $"winget install --id {package.Id}\n{Trim(output)}\n\n重新检测：{after.Summary}";
        return code == 0 && found
            ? VcsRunResult.Ok(action, string.Empty, $"{package.Name} 已安装", text)
            : VcsRunResult.Fail(action, string.Empty, $"{package.Name} 安装未完成",
                text + $"\n\n也可以手动安装：{package.Url}");
    }

    /// <summary>
    /// Turns LFS on for one repository only. A global <c>git lfs install</c> would
    /// rewrite the user's own git config, and a <c>migrate</c> would rewrite history
    /// that may already be pushed, so neither is offered from here.
    /// </summary>
    public async Task<VcsRunResult> EnableLfsAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var tools = await DetectToolsAsync(ct);
        if (tools.GitPath == null)
            return VcsRunResult.Fail("lfs", path, "未安装 git", "没有在本机找到 git.exe。");

        var gitDir = ResolveGitDir(path);
        if (gitDir == null)
            return VcsRunResult.Fail("lfs", path, "这个目录不是 git 仓库",
                "先点「接入版本托管」建立仓库，再启用 LFS。");

        if (!tools.GitLfsFound)
            return VcsRunResult.Fail("lfs", path, "未安装 Git LFS",
                "git 里没有 git-lfs。安装 Git for Windows 时勾选 “Git LFS” 组件，或单独安装 git-lfs 后重新检测。");

        var log = new StringBuilder();
        var (installCode, installOut) = await RunProcAsync(tools.GitPath, path,
            ["lfs", "install", "--local"], QuickTimeout, Encoding.UTF8, ct);
        log.AppendLine($"git lfs install --local\n{Trim(installOut)}");
        if (installCode != 0)
            return VcsRunResult.Fail("lfs", path, "启用 LFS 失败", log.ToString());

        log.Append(await WriteIgnoreFileAsync(path, ".gitattributes", UnityGitattributes));

        var (trackCode, trackOut) = await RunProcAsync(tools.GitPath, path,
            ["lfs", "status", "--json"], QuickTimeout, Encoding.UTF8, ct);
        if (trackCode == 0) log.AppendLine($"\ngit lfs status\n{Trim(trackOut)}");

        return VcsRunResult.Ok("lfs", path, "已为该仓库启用 Git LFS", log.ToString().Trim());
    }

    public async Task<VcsRunResult> DisableLfsAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var tools = await DetectToolsAsync(ct);
        if (tools.GitPath == null)
            return VcsRunResult.Fail("unlfs", path, "未安装 git", "没有在本机找到 git.exe。");

        if (ResolveGitDir(path) == null)
            return VcsRunResult.Fail("unlfs", path, "这个目录不是 git 仓库", path);

        var log = new StringBuilder();
        var (code, output) = await RunProcAsync(tools.GitPath, path,
            ["lfs", "uninstall", "--local"], QuickTimeout, Encoding.UTF8, ct);
        log.AppendLine($"git lfs uninstall --local\n{Trim(output)}");
        if (code != 0)
            return VcsRunResult.Fail("unlfs", path, "禁用 LFS 失败", log.ToString().Trim());

        // The attribute file this app wrote exists only to point at those filters. Leaving it
        // behind would have git ask for a clean/smudge command that is no longer configured.
        var attributes = Path.Combine(path, ".gitattributes");
        if (File.Exists(attributes))
        {
            var text = ReadAll(attributes);
            if (text.Contains("由 Akashic 生成", StringComparison.Ordinal))
            {
                try
                {
                    File.Delete(attributes);
                    log.AppendLine("\n已删除 Akashic 写入的 .gitattributes。");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"\n.gitattributes 删除失败：{ex.Message}");
                }
            }
            else
            {
                log.AppendLine("\n.gitattributes 不是本应用写的，保持原样；需要的话请自行删掉其中的 filter=lfs 行。");
            }
        }

        return VcsRunResult.Ok("unlfs", path, "已为该仓库禁用 Git LFS", log.ToString().Trim());
    }

    public async Task<VcsRunResult> SetRemoteAsync(string projectPath, string url, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var tools = await DetectToolsAsync(ct);
        if (tools.GitPath == null)
            return VcsRunResult.Fail("remote", path, "未安装 git", "没有在本机找到 git.exe。");

        var gitDir = ResolveGitDir(path);
        if (gitDir == null)
            return VcsRunResult.Fail("remote", path, "这个目录不是 git 仓库",
                "先点「接入 git」建立仓库，再填远程地址。");

        if (!IsSafeRemoteUrl(url, out var reason))
            return VcsRunResult.Fail("remote", path, "远程地址不可用", reason);

        var current = ReadOriginUrl(ReadAll(GitConfigPath(gitDir) ?? Path.Combine(gitDir, "config")));
        var args = current == null
            ? new[] { "remote", "add", "origin", url.Trim() }
            : new[] { "remote", "set-url", "origin", url.Trim() };

        var (code, output) = await RunProcAsync(tools.GitPath, path, args, QuickTimeout, Encoding.UTF8, ct);
        if (code != 0)
            return VcsRunResult.Fail("remote", path, "设置远程失败", Trim(output));

        // Tell the user which commits belong where without waiting for a push.
        await RunProcAsync(tools.GitPath, path, ["fetch", "--quiet", "origin"], NetworkTimeout,
            Encoding.UTF8, CancellationToken.None);

        var state = await ReadStateAsync(path, true, ct);
        return VcsRunResult.Ok("remote", path, $"远程已设置：{state.Remote}", Trim(output));
    }

    public async Task<VcsRunResult> SubmitAsync(
        string projectPath, string message, bool pushAfter, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var comment = (message ?? string.Empty).Trim();
        if (comment.Length == 0)
            return VcsRunResult.Fail("submit", path, "缺少提交说明", "请填写这次改动的说明。");

        var state = await ReadStateAsync(path, ct: ct);
        var tools = await DetectToolsAsync(ct);

        if (state.Kind == "uvcs")
        {
            if (tools.CmPath == null)
                return VcsRunResult.Fail("submit", path, "未安装 Unity Version Control", "没有在本机找到 cm.exe。");

            var (code, output) = await RunProcAsync(tools.CmPath, path,
                ["checkin", "--all", $"-c={comment}"], NetworkTimeout, null, ct);
            if (code != 0) return VcsRunResult.Fail("submit", path, "UVCS 提交失败", Trim(output));

            var log = $"cm checkin --all\n{Trim(output)}";
            if (pushAfter) log += "\n\n" + (await RunAsync(tools.CmPath, path, ["push"], NetworkTimeout, null, ct));
            return VcsRunResult.Ok("submit", path, pushAfter ? "已提交并推送" : "已提交到 UVCS", log);
        }

        if (state.Kind != "git")
            return VcsRunResult.Fail("submit", path, "工程尚未接入版本托管",
                "先在上面点「接入 git」或「接入 Unity Version Control」。");

        if (tools.GitPath == null)
            return VcsRunResult.Fail("submit", path, "未安装 git", "没有在本机找到 git.exe。");

        if (!File.Exists(Path.Combine(path, ".gitignore")) && Directory.Exists(Path.Combine(path, "Library")))
            return VcsRunResult.Fail("submit", path, "还没有 .gitignore",
                "缺少 .gitignore 时 git add 会把 Library 整个塞进历史。先点「生成 .gitignore」再提交。");

        var staged = await RunProcAsync(tools.GitPath, path, ["add", "-A"],
            TimeSpan.FromMinutes(10), Encoding.UTF8, ct);
        if (staged.ExitCode != 0)
            return VcsRunResult.Fail("submit", path, "git add 失败", Trim(staged.Output));

        var committed = await RunProcAsync(tools.GitPath, path, ["commit", $"--message={comment}"],
            NetworkTimeout, Encoding.UTF8, ct);
        var text = $"git add -A\n{Trim(staged.Output)}\n\ngit commit\n{Trim(committed.Output)}";

        if (committed.ExitCode != 0)
        {
            if (committed.Output.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                return VcsRunResult.Ok("submit", path, "没有需要提交的改动", text);

            var hint = committed.Output.Contains("author identity unknown", StringComparison.OrdinalIgnoreCase)
                ? "\n\n还没有配置提交身份：请在系统终端执行 git config --global user.name 与 user.email。"
                : string.Empty;
            return VcsRunResult.Fail("submit", path, "git commit 失败", text + hint);
        }

        if (pushAfter)
        {
            var pushed = await RunProcAsync(tools.GitPath, path, ["push", "--set-upstream", "origin", "HEAD"],
                NetworkTimeout, Encoding.UTF8, ct);
            text += $"\n\ngit push\n{Trim(pushed.Output)}";
            if (pushed.ExitCode != 0)
                return VcsRunResult.Fail("submit", path, "已提交，但推送失败", text);
        }

        return VcsRunResult.Ok("submit", path, pushAfter ? "已提交并推送" : "已提交到本地仓库", text);
    }

    public async Task<VcsRunResult> UpdateAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var state = await ReadStateAsync(path, ct: ct);
        var tools = await DetectToolsAsync(ct);

        if (state.Kind == "git" && tools.GitPath != null)
        {
            var (code, output) = await RunProcAsync(tools.GitPath, path, ["pull", "--ff-only"],
                NetworkTimeout, Encoding.UTF8, ct);
            return code == 0
                ? VcsRunResult.Ok("update", path, "已拉取更新", Trim(output))
                : VcsRunResult.Fail("update", path, "git pull 失败", Trim(output));
        }

        if (state.Kind is "uvcs" or "both" && tools.CmPath != null)
        {
            var (code, output) = await RunProcAsync(tools.CmPath, path, ["update"], NetworkTimeout, null, ct);
            return code == 0
                ? VcsRunResult.Ok("update", path, "已更新 workspace", Trim(output))
                : VcsRunResult.Fail("update", path, "cm update 失败", Trim(output));
        }

        return VcsRunResult.Fail("update", path, "工程尚未接入版本托管", "先接入 git 或 Unity Version Control。");
    }

    public async Task<VcsRunResult> PushAsync(string projectPath, CancellationToken ct = default)
    {
        var path = Path.GetFullPath(projectPath);
        var state = await ReadStateAsync(path, ct: ct);
        var tools = await DetectToolsAsync(ct);

        if (state.Kind == "git" && tools.GitPath != null)
        {
            var (code, output) = await RunProcAsync(tools.GitPath, path, ["push", "--set-upstream", "origin", "HEAD"],
                NetworkTimeout, Encoding.UTF8, ct);
            return code == 0
                ? VcsRunResult.Ok("push", path, "已推送", Trim(output))
                : VcsRunResult.Fail("push", path, "git push 失败", Trim(output));
        }

        if (state.Kind is "uvcs" or "both" && tools.CmPath != null)
        {
            var (code, output) = await RunProcAsync(tools.CmPath, path, ["push"], NetworkTimeout, null, ct);
            return code == 0
                ? VcsRunResult.Ok("push", path, "已推送分支", Trim(output))
                : VcsRunResult.Fail("push", path, "cm push 失败", Trim(output));
        }

        return VcsRunResult.Fail("push", path, "工程尚未接入版本托管", "先接入 git 或 Unity Version Control。");
    }

    // ── Process plumbing ──────────────────────────────────────────────────────────────────────

    private static async Task<string> RunAsync(
        string exe, string workDir, string[] args, TimeSpan timeout, Encoding? encoding, CancellationToken ct)
    {
        var (code, output) = await RunProcAsync(exe, workDir, args, timeout, encoding, ct);
        return $"{Path.GetFileName(exe)} {string.Join(' ', args)}\n{Trim(output)}\n{(code == 0 ? "成功" : $"退出码 {code}")}";
    }

    private static async Task<(int ExitCode, string Output)> RunProcAsync(
        string exe, string workDir, IReadOnlyList<string> args, TimeSpan timeout, Encoding? encoding,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Directory.Exists(workDir) ? workDir : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };

        if (encoding != null)
        {
            psi.StandardOutputEncoding = encoding;
            psi.StandardErrorEncoding = encoding;
        }

        // A push to a private remote should open the credential window, not hang waiting for a
        // terminal that was never attached.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";

        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) return (-1, $"无法启动 {Path.GetFileName(exe)}");
        }
        catch (Exception ex)
        {
            return (-1, $"无法启动 {Path.GetFileName(exe)}: {ex.Message}");
        }

        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        proc.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            var why = ct.IsCancellationRequested ? "已取消：" : "执行超时：";
            return (-2, $"{why}{Path.GetFileName(exe)} {string.Join(' ', args)}");
        }

        var text = await stdout;
        var error = await stderr;
        var combined = string.IsNullOrWhiteSpace(error) ? text : $"{text}\n{error}";
        return (proc.ExitCode, combined.Trim());
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clean = text.Replace("\r\n", "\n").Trim();
        return clean.Length > 6000 ? clean[..6000] + "\n…（输出过长已截断）" : clean;
    }

    private static string SafeName(string raw)
    {
        var clean = new StringBuilder();
        foreach (var c in raw)
            clean.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');

        var name = clean.ToString().Trim('.', '_');
        return name.Length == 0 ? "AkashicWorkspace" : name;
    }

    private static bool IsSafeRemoteUrl(string? url, out string reason)
    {
        reason = string.Empty;
        var text = (url ?? string.Empty).Trim();

        if (text.Length == 0) { reason = "远程地址为空。"; return false; }
        if (text.StartsWith('-')) { reason = "远程地址不能以 - 开头。"; return false; }
        if (text.Any(char.IsWhiteSpace)) { reason = "远程地址不能包含空格。"; return false; }

        var ok = text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                 || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || text.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                 || text.StartsWith("git://", StringComparison.OrdinalIgnoreCase)
                 || System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\w.-]+@[\w.-]+:.+$");

        if (!ok) reason = "只接受 https:// 、ssh:// 、git:// 或 git@主机:路径 形式的地址。";
        return ok;
    }

    // ── Ignore files ──────────────────────────────────────────────────────────────────────────

    private static async Task<string> WriteIgnoreFileAsync(string path, string fileName, string content)
    {
        var target = Path.Combine(path, fileName);
        if (File.Exists(target)) return $"\n{fileName} 已存在，未改动。";

        try
        {
            await File.WriteAllTextAsync(target, content, new UTF8Encoding(false));
            return $"\n已写入 {fileName}。";
        }
        catch (Exception ex)
        {
            return $"\n{fileName} 写入失败: {ex.Message}";
        }
    }

    private const string UnityGitignore = """
        # 由 Akashic 生成：Unity 工程里可重建的目录不进版本库。
        # 需要追踪某个例外时，直接在这里删掉对应行。
        [Ll]ibrary/
        [Tt]emp/
        [Oo]bj/
        [Bb]uild/
        [Bb]uilds/
        [Ll]ogs/
        [Mm]emoryCaptures/
        [Uu]serSettings/
        [Rr]ecordings/

        # Unity 的包缓存与本地覆盖不该进历史，manifest 才是凭据。
        /[Pp]ackages/[Mm]anifest.json.*
        /[Aa]ssets/AssetStoreTools*
        /[Aa]ssets/Plugins/Editor/JetBrains*

        # 视觉脚本 / Udon 生成的中间产物
        /[Aa]ssets/UdonSharp/*/
        /Assets/Udon/Logs/

        # IDE 与系统杂物
        ExportedObj/
        .consulo/
        .idea/
        .vs/
        .vscode/
        *.csproj
        *.unityproj
        *.sln
        *.suo
        *.tmp
        *.user
        *.userprefs
        *.pidb
        *.booproj
        *.svd
        *.pdb
        *.mdb
        *.opendb
        *.VC.db
        *.pidb.meta
        *.pdb.meta
        *.mdb.meta
        sysinfo.txt
        .DS_Store

        # 构建产物
        *.apk
        *.aab
        *.unitypackage
        *.app
        crashlytics-build.properties
        """;

    /// <summary>
    /// Binary art goes through LFS; the .meta sidecars stay in the repository because
    /// they are text and their diffs are what a merge conflict actually needs.
    /// </summary>
    private const string UnityGitattributes = """
        # 由 Akashic 生成：Unity / VRChat 工程里的二进制资源交给 Git LFS。
        # 需要增减类型时，直接改这里，然后重新提交。
        *.psd filter=lfs diff=lfs merge=lfs -text
        *.psb filter=lfs diff=lfs merge=lfs -text
        *.tga filter=lfs diff=lfs merge=lfs -text
        *.png filter=lfs diff=lfs merge=lfs -text
        *.jpg filter=lfs diff=lfs merge=lfs -text
        *.jpeg filter=lfs diff=lfs merge=lfs -text
        *.gif filter=lfs diff=lfs merge=lfs -text
        *.bmp filter=lfs diff=lfs merge=lfs -text
        *.tif filter=lfs diff=lfs merge=lfs -text
        *.tiff filter=lfs diff=lfs merge=lfs -text
        *.exr filter=lfs diff=lfs merge=lfs -text
        *.hdr filter=lfs diff=lfs merge=lfs -text
        *.dds filter=lfs diff=lfs merge=lfs -text
        *.kra filter=lfs diff=lfs merge=lfs -text

        *.fbx filter=lfs diff=lfs merge=lfs -text
        *.obj filter=lfs diff=lfs merge=lfs -text
        *.blend filter=lfs diff=lfs merge=lfs -text
        *.max filter=lfs diff=lfs merge=lfs -text
        *.ma filter=lfs diff=lfs merge=lfs -text
        *.mb filter=lfs diff=lfs merge=lfs -text
        *.gltf filter=lfs diff=lfs merge=lfs -text
        *.glb filter=lfs diff=lfs merge=lfs -text
        *.vrm filter=lfs diff=lfs merge=lfs -text
        *.pmx filter=lfs diff=lfs merge=lfs -text
        *.pmd filter=lfs diff=lfs merge=lfs -text

        *.wav filter=lfs diff=lfs merge=lfs -text
        *.mp3 filter=lfs diff=lfs merge=lfs -text
        *.ogg filter=lfs diff=lfs merge=lfs -text
        *.aif filter=lfs diff=lfs merge=lfs -text
        *.aiff filter=lfs diff=lfs merge=lfs -text
        *.flac filter=lfs diff=lfs merge=lfs -text

        *.mp4 filter=lfs diff=lfs merge=lfs -text
        *.mov filter=lfs diff=lfs merge=lfs -text
        *.avi filter=lfs diff=lfs merge=lfs -text
        *.webm filter=lfs diff=lfs merge=lfs -text
        *.mkv filter=lfs diff=lfs merge=lfs -text

        *.unitypackage filter=lfs diff=lfs merge=lfs -text
        *.dll filter=lfs diff=lfs merge=lfs -text
        *.so filter=lfs diff=lfs merge=lfs -text
        *.a filter=lfs diff=lfs merge=lfs -text
        *.bundle filter=lfs diff=lfs merge=lfs -text
        *.zip filter=lfs diff=lfs merge=lfs -text
        *.otf filter=lfs diff=lfs merge=lfs -text
        *.ttf filter=lfs diff=lfs merge=lfs -text
        *.sbsar filter=lfs diff=lfs merge=lfs -text
        *.vrm.meta text
        *.meta text
        *.cs text
        """;

    private const string PlasticIgnore = """
        # 由 Akashic 生成：UVCS 不追踪 Unity 里可重建的目录。
        Library/
        Temp/
        Obj/
        Build/
        Builds/
        Logs/
        MemoryCaptures/
        UserSettings/
        Recordings/
        .vs/
        .vscode/
        .idea/
        *.csproj
        *.sln
        *.user
        *.tmp
        """;
}
