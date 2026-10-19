using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services;

public sealed record GitHubIdentity
{
    public required string Login { get; init; }
    public long UserId { get; init; }
    public string AvatarUrl { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public List<string> Scopes { get; init; } = [];

    public bool HasGistScope => Scopes.Count == 0 ||
        Scopes.Any(scope => scope.Contains("gist", StringComparison.OrdinalIgnoreCase));

    public bool SignedIn => !string.IsNullOrEmpty(Login) && !string.IsNullOrEmpty(Token);
}

public sealed class GitHubCli
{
    public const string GistFile = "akashic-sync.json";

    public const string GistDescription = "Akashic 配置同步（AES-256-GCM 端到端加密，明文不上传）";

    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);

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

    public void Forget() => _identityTask = null;

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

    public Task<(bool ok, string error)> DownloadAsync(string apiPath, string target,
        CancellationToken ct = default) =>
        RunToFileAsync(Find(), ["api", apiPath], target, TransferTimeout, ct);

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

            var args = string.IsNullOrWhiteSpace(gistId)
                ? new List<string> { "api", "gists", "--input", temp, "--jq", ".id" }
                : new List<string>
                {
                    "api", $"gists/{gistId.Trim()}", "--method", "PATCH", "--input", temp, "--jq", ".id"
                };

            var (code, text) = await RunAsync(exe, args, TransferTimeout, ct).ConfigureAwait(false);

            if (code != 0 && !string.IsNullOrWhiteSpace(gistId) && NotFound(text))
            {
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
