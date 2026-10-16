using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services;

/// <summary>Who the GitHub CLI is signed in as, and what that token may do.</summary>
public sealed record GitHubIdentity
{
    public required string Login { get; init; }
    public long UserId { get; init; }
    public string AvatarUrl { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// The OAuth token gh keeps in the keyring. Read only as proof that somebody is signed in, and
    /// never handed to another endpoint by this app — every GitHub call goes through gh itself.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    public List<string> Scopes { get; init; } = [];

    /// <summary>
    /// Whether this token may write gists. gh reports classic scopes as "gist" and GitHub App
    /// permissions as "Gist access"; a token it reports no scopes for (a fine-grained PAT, or an App
    /// token it cannot describe) is not blocked here — the gist call itself is the real oracle, and
    /// its error is clearer than a wrong guess made in advance.
    /// </summary>
    public bool HasGistScope => Scopes.Count == 0 ||
        Scopes.Any(scope => scope.Contains("gist", StringComparison.OrdinalIgnoreCase));

    /// <summary>False when gh is installed but nothing is signed in, or the token was revoked.</summary>
    public bool SignedIn => !string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Token);
}

/// <summary>
/// The one door this app uses for GitHub. Everything goes through the GitHub CLI rather than
/// hand-rolled HTTP: gh already holds an authorised token in the Windows keyring, so private
/// repositories and the gist API work without Akashic ever asking for a password, and the rate
/// limit follows the user's own account instead of the shared anonymous one.
///
/// Sign-in arrives here too: the browser flow in <see cref="GitHubLoopbackAuth"/> writes its token
/// into the CLI, so one authorisation covers private repositories and the sync gist alike, and no
/// second password prompt is ever needed.
///
/// Every call is a process spawn, so results that the UI reads on every paint are cached in the
/// <see cref="_identityTask"/> rather than re-run.
/// </summary>
public sealed class GitHubCli
{
    /// <summary>The single file the sync payload lives in, inside a private gist.</summary>
    public const string GistFile = "akashic-sync.json";

    public const string GistDescription = "Akashic 配置同步（AES-256-GCM 端到端加密，明文不上传）";

    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Where a winget or manual install leaves gh.exe.</summary>
    public static readonly string[] InstallCandidates =
    [
        @"C:\Program Files\GitHub CLI\gh.exe",
        @"C:\Program Files (x86)\GitHub CLI\gh.exe",
        Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Programs\GitHub CLI\gh.exe")
    ];

    private Task<GitHubIdentity?>? _identityTask;

    public static string? Find()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "gh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }

        return InstallCandidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Drops the memo so a fresh login or install is picked up without a restart.</summary>
    public void Forget() => _identityTask = null;

    /// <summary>
    /// Hands a token that this app minted itself straight to the CLI through
    /// <c>gh auth login --with-token</c>, so one browser authorisation also signs gh in: repository
    /// downloads and the gist API keep going through this one door, and the token lands in the same
    /// keyring entry a manual <c>gh auth login</c> would have written.
    /// </summary>
    public async Task<(bool ok, string error)> LoginWithTokenAsync(string token, CancellationToken ct = default)
    {
        var exe = Find();
        if (exe is null) return (false, "未检测到 GitHub CLI");
        if (string.IsNullOrWhiteSpace(token)) return (false, "没有可用的令牌");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "auth", "login", "--hostname", "github.com", "--with-token" })
            psi.ArgumentList.Add(arg);

        psi.Environment["GH_PAGER"] = "cat";
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";

        try
        {
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 GitHub CLI");

            // Drained before the token is written: a pipe nobody reads would keep gh from finishing.
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);

            await proc.StandardInput.WriteAsync(token + "\n").ConfigureAwait(false);
            proc.StandardInput.Close();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(QuickTimeout);

            try
            {
                await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                return (false, "GitHub CLI 写入登录态超时");
            }

            var text = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            var merged = string.IsNullOrWhiteSpace(error) ? text : $"{text}\n{error}";

            // The keyring entry changed, so any cached identity is now about the old account.
            Forget();

            return proc.ExitCode == 0
                ? (true, string.Empty)
                : (false, Trim(merged).Length > 0 ? Trim(merged) : $"gh auth login 失败（退出码 {proc.ExitCode}）");
        }
        catch (Exception ex)
        {
            return (false, $"无法写入 GitHub CLI 登录态：{ex.Message}");
        }
    }

    public Task<GitHubIdentity?> IdentityAsync(CancellationToken ct = default)
    {
        // A null or signed-out answer is not memoised: installing gh or running
        // `gh auth login` has to take effect without restarting the sidecar.
        if (_identityTask is { IsCompletedSuccessfully: true } cached && cached.Result?.SignedIn == true)
            return cached;

        return _identityTask = ReadAsync(ct);
    }

    private async Task<GitHubIdentity?> ReadAsync(CancellationToken ct)
    {
        var exe = Find();
        if (exe is null) return null;

        var version = ShortVersion(await RunAsync(exe, ["--version"], QuickTimeout, ct).ConfigureAwait(false));

        var (code, text) = await RunAsync(exe,
        [
            "api", "user",
            "--jq", ".login + \"|\" + (.id | tostring) + \"|\" + (.avatar_url // \"\")"
        ], QuickTimeout, ct).ConfigureAwait(false);

        if (code != 0)
        {
            // gh answers with the CLI present but nobody signed in; the settings page says so.
            return new GitHubIdentity { Login = string.Empty, UserId = 0, Version = version };
        }

        var parts = text.Trim().Split('|', 3);
        var login = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        long.TryParse(parts.Length > 1 ? parts[1].Trim() : "0", out var id);
        var avatar = parts.Length > 2 ? parts[2].Trim() : string.Empty;

        var token = (await RunAsync(exe, ["auth", "token"], QuickTimeout, ct).ConfigureAwait(false)).text.Trim();
        var scopes = ReadScopes(await RunAsync(exe, ["auth", "status"], QuickTimeout, ct).ConfigureAwait(false));

        return new GitHubIdentity
        {
            Login = login,
            UserId = id,
            AvatarUrl = avatar,
            Version = version,
            Token = token.StartsWith("gh", StringComparison.Ordinal) ? token : string.Empty,
            Scopes = scopes
        };
    }

    /// <summary>gh prints "Token scopes: 'gist', 'repo'"; only the quoted runs are scopes.</summary>
    private static List<string> ReadScopes((int code, string text) status)
    {
        var scopes = new List<string>();
        if (status.code != 0) return scopes;

        foreach (var line in status.text.Split('\n'))
        {
            if (!line.Contains("Token scopes", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (Match match in Regex.Matches(line, "'([^']*)'"))
            {
                var value = match.Groups[1].Value.Trim();
                if (value.Length > 0) scopes.Add(value);
            }

            break;
        }

        return scopes;
    }

    private static string ShortVersion((int code, string text) raw)
    {
        if (raw.code != 0) return string.Empty;

        foreach (var piece in raw.text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (piece.Contains('.') && char.IsDigit(piece[0])) return piece.TrimStart('v');

        return string.Empty;
    }

    public static async Task<(int code, string text)> RunAsync(
        string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args) psi.ArgumentList.Add(arg);

        // No pager, no interactive prompt, no update nag: a spawn that waits for a keypress
        // would otherwise sit on the timeout and look like a network failure.
        psi.Environment["GH_PAGER"] = "cat";
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";

        try
        {
            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("无法启动 GitHub CLI");

            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            proc.StandardInput.Close();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            try
            {
                await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                return (-2, $"GitHub CLI 超时：gh {string.Join(' ', args)}");
            }

            var text = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            var merged = string.IsNullOrWhiteSpace(error) ? text : $"{text}\n{error}";

            return (proc.ExitCode, Trim(merged));
        }
        catch (Exception ex)
        {
            return (-1, $"无法运行 GitHub CLI：{ex.Message}");
        }
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
        var clean = text.Trim();
        return clean.Length <= 6000 ? clean : clean[..6000] + "\n…（输出过长已截断）";
    }

    /// <summary>Runs `gh api <path>` and answers the body, or null when the call failed.</summary>
    public async Task<string?> ApiAsync(string path, string? jq = null, CancellationToken ct = default)
    {
        var exe = Find();
        if (exe is null) return null;

        var args = new List<string> { "api", path };
        if (jq is not null)
        {
            args.Add("--jq");
            args.Add(jq);
        }

        var (code, text) = await RunAsync(exe, args, QuickTimeout, ct).ConfigureAwait(false);
        return code == 0 ? text : null;
    }

    /// <summary>
    /// Streams `gh api <path>` straight into a file. The zipball endpoint answers with the
    /// archive bytes on stdout, and a gist read answers with the whole encrypted bundle —
    /// neither may be captured as text, because <see cref="Trim"/> would cut them off.
    /// </summary>
    public Task<(bool ok, string error)> DownloadAsync(string apiPath, string target,
        CancellationToken ct = default) =>
        RunToFileAsync(Find(), ["api", apiPath], target, TransferTimeout, ct);

    /// <summary>
    /// Runs gh with stdout wired to a file. The text capture used by every other call is
    /// deliberately not applied here: its 6 KB cap is for human-readable logs.
    /// </summary>
    private static async Task<(bool ok, string error)> RunToFileAsync(
        string? exe, IReadOnlyList<string> args, string target, TimeSpan timeout, CancellationToken ct)
    {
        if (exe is null) return (false, "未检测到 GitHub CLI");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["GH_PAGER"] = "cat";
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";

        var temp = target + ".part";

        try
        {
            using var proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("无法启动 GitHub CLI");

            await using (var file = File.Create(temp))
            await using (var stream = proc.StandardOutput.BaseStream)
                await stream.CopyToAsync(file, ct).ConfigureAwait(false);

            var error = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            try
            {
                await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                return (false, "GitHub CLI 传输超时");
            }

            if (proc.ExitCode != 0)
            {
                TryDelete(temp);
                return (false, Trim(error).Length > 0
                    ? Trim(error)
                    : $"gh {args[0]} 失败（退出码 {proc.ExitCode}）");
            }

            if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
            {
                TryDelete(temp);
                return (false, "GitHub CLI 没有返回任何数据");
            }

            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            return (false, $"无法通过 GitHub CLI 传输：{ex.Message}");
        }
    }

    /// <summary>
    /// Creates the gist when <paramref name="gistId"/> is empty, otherwise patches the one already
    /// in use. The body goes in through a temp file because a several-megabyte payload will not fit
    /// on a command line.
    /// </summary>
    public async Task<(string? id, string error)> PushGistAsync(
        string content, string? gistId, CancellationToken ct = default)
    {
        var exe = Find();
        if (exe is null) return (null, "未检测到 GitHub CLI");

        var identity = await IdentityAsync(ct).ConfigureAwait(false);
        if (identity?.SignedIn != true)
            return (null, "GitHub CLI 未登录，请在 设置 → 配置同步 点「使用浏览器登录 GitHub」");
        if (!identity.HasGistScope)
            return (null, $"{identity.Login} 的令牌没有 gist 权限，重新用浏览器登录一次并勾上 gist");

        // Bytes, not a string written with Encoding.UTF8: that pairing puts a BOM on the front
        // of the file and GitHub answers with "Problems parsing JSON".
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            description = GistDescription,
            @public = false,
            files = new Dictionary<string, object>
            {
                [GistFile] = new { content }
            }
        });

        var temp = Path.Combine(Path.GetTempPath(), $"akashic-gist-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllBytesAsync(temp, body, ct).ConfigureAwait(false);

            // --jq .id keeps the reply to one line. Without it gh echoes the whole gist back,
            // which is the entire encrypted bundle, and the text capture would cut it off
            // mid-string while the gist had already been created.
            var args = string.IsNullOrWhiteSpace(gistId)
                ? new List<string> { "api", "gists", "--input", temp, "--jq", ".id" }
                : new List<string>
                {
                    "api", $"gists/{gistId.Trim()}", "--method", "PATCH", "--input", temp, "--jq", ".id"
                };

            var (code, text) = await RunAsync(exe, args, TransferTimeout, ct).ConfigureAwait(false);

            if (code != 0 && !string.IsNullOrWhiteSpace(gistId) && NotFound(text))
            {
                // The stored id points at a gist that is gone — deleted by hand, or left over
                // from another account. Creating a new one beats failing with a 404 the user
                // cannot act on.
                (code, text) = await RunAsync(exe,
                    new List<string> { "api", "gists", "--input", temp, "--jq", ".id" },
                    TransferTimeout, ct).ConfigureAwait(false);
            }

            if (code != 0) return (null, Trim(text));

            var id = Unquote(text);
            return string.IsNullOrWhiteSpace(id)
                ? (null, "GitHub 没有返回 gist id")
                : (id, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, $"上传 gist 失败：{ex.Message}");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Reads the sync file back out of a gist. The bundle is far larger than the text capture
    /// allows, so gh writes it straight to a temp file instead.
    /// </summary>
    public async Task<string?> PullGistAsync(string gistId, CancellationToken ct = default)
    {
        var exe = Find();
        if (exe is null || string.IsNullOrWhiteSpace(gistId)) return null;

        var temp = Path.Combine(Path.GetTempPath(), $"akashic-gist-{Guid.NewGuid():N}.json");
        try
        {
            var (ok, _) = await RunToFileAsync(exe,
            [
                "api", $"gists/{gistId.Trim()}",
                "--jq", $".files[\"{GistFile}\"].content // \"\""
            ], temp, TransferTimeout, ct).ConfigureAwait(false);

            if (!ok) return null;

            var raw = await File.ReadAllTextAsync(temp, ct).ConfigureAwait(false);
            return Unquote(raw.Trim());
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// gojq prints a top-level string bare, but an older gh quotes it as a JSON literal. Both
    /// arrive here.
    /// </summary>
    private static string? Unquote(string raw)
    {
        if (raw.Length == 0) return null;
        if (!raw.StartsWith('"')) return raw;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString()
                : raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    /// <summary>
    /// Finds the sync gist on an account that has the backup but not the id — a second machine
    /// signed into the same GitHub account should not have to hunt for it.
    /// </summary>
    public async Task<string?> FindGistAsync(CancellationToken ct = default)
    {
        var exe = Find();
        if (exe is null) return null;

        var (code, text) = await RunAsync(exe,
        [
            "api", "gists?per_page=100",
            "--jq", $"[.[] | select(.files | has(\"{GistFile}\")) | .id] | first // \"\""
        ], QuickTimeout, ct).ConfigureAwait(false);

        if (code != 0) return null;

        var raw = text.Trim();
        if (raw.Length == 0 || raw == "null") return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : raw;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static bool NotFound(string text) =>
        text.Contains("Not Found", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
