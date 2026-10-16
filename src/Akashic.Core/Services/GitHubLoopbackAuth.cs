using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Akashic.Core.Services;

public sealed record GitHubLoginResult
{
    public required bool Ok { get; init; }
    public long UserId { get; init; }
    public string Login { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string Guidance { get; init; } = string.Empty;

    /// <summary>The page handed to the browser, so the UI can offer it when no browser opened.</summary>
    public string AuthorizeUrl { get; init; } = string.Empty;
}

/// <summary>
/// Signs a user into GitHub the way VS Code and JetBrains do: open GitHub's own authorisation page in
/// the user's browser, catch the redirect on <c>http://127.0.0.1:&lt;random port&gt;/callback/</c>,
/// and trade the code for a token here.
///
/// What the user does is click one green <em>Authorize</em> button. They never type an account, a
/// password, an 8-character device code, or a second sync passphrase into Akashic, and Akashic never
/// sees those secrets — the browser already holds the GitHub session.
///
/// PKCE (<c>code_verifier</c> + S256 challenge) binds the code to this socket and the random
/// <c>state</c> binds the callback to this attempt, so a request that reaches the port from anywhere
/// else is answered and ignored. The token then goes into the GitHub CLI's own credential store, which
/// keeps every GitHub call in this app behind the single <see cref="GitHubCli"/> door and makes one
/// authorisation cover private repositories as well as the sync gist.
///
/// The exchange also sends the client secret that <see cref="GitHubOAuthApp"/> ships with, because
/// GitHub documents it as required and does not distinguish public from confidential clients: PKCE is
/// an extra binding there rather than a replacement.
/// </summary>
public sealed class GitHubLoopbackAuth
{
    /// <summary>
    /// Redirect path. The OAuth App's 授权回调网址 has to be <c>http://127.0.0.1/callback/</c>: GitHub
    /// matches the registered URL as a prefix of the redirect it is sent and lets the port vary, which
    /// is what makes a random loopback port legal (RFC 8252 §7.3).
    /// </summary>
    public const string CallbackPath = "/callback/";

    public const string RegisteredCallback = GitHubOAuthApp.CallbackUrl;

    /// <summary>
    /// Classic scopes. <c>gist</c> is what config sync needs; <c>repo</c> covers the private
    /// repositories the package browser reads. <c>read:org</c> is not optional in practice: the GitHub
    /// CLI refuses to store a token that lacks it ("error validating token: missing required scope
    /// 'read:org'"), and gh is where this app keeps the credential.
    /// </summary>
    public static readonly string[] Scopes = ["repo", "read:org", "gist"];

    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    private readonly GitHubCli _gitHub;
    private readonly string _authority;
    private readonly string _apiAuthority;

    /// <summary>
    /// Both authorities exist so the self-test can run the whole flow against a local mock; shipped
    /// Akashic always lands on github.com and api.github.com.
    /// </summary>
    public GitHubLoopbackAuth(GitHubCli gitHub,
        string authority = "https://github.com",
        string? apiAuthority = null)
    {
        _gitHub = gitHub;
        _authority = authority.TrimEnd('/');
        _apiAuthority = (apiAuthority ?? "https://api.github.com").TrimEnd('/');
    }

    public async Task<GitHubLoginResult> LoginAsync(
        string clientId,
        string? clientSecret = null,
        TimeSpan? window = null,
        Action<string>? openBrowser = null,
        CancellationToken ct = default)
    {
        clientId = (clientId ?? string.Empty).Trim();
        clientSecret = (clientSecret ?? string.Empty).Trim();

        if (clientId.Length == 0)
            return Fail("这份 Akashic 没有带上可用的 GitHub 登录凭据",
                "请重新下载安装；开发环境可以用 AKASHIC_GITHUB_CLIENT_ID / _SECRET 覆盖内置的 OAuth App。");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        HttpListener listener;
        int port;
        try
        {
            (listener, port) = OpenLoopback();
        }
        catch (Exception ex)
        {
            return Fail("无法在本机开启回调端口", ex.Message);
        }

        using (listener)
        {
            var redirect = $"http://127.0.0.1:{port}{CallbackPath}";
            var authorizeUrl = BuildAuthorizeUrl(clientId, redirect, state, challenge);

            try
            {
                (openBrowser ?? OpenInBrowser)(authorizeUrl);
            }
            catch (Exception ex)
            {
                return Fail("无法打开浏览器", $"{ex.Message}\n请手动打开下面的地址完成授权：\n{authorizeUrl}",
                    authorizeUrl);
            }

            var (code, error) = await WaitForCodeAsync(listener, state, window ?? DefaultWindow, ct)
                .ConfigureAwait(false);
            if (code.Length == 0) return Fail(error, string.Empty, authorizeUrl);

            // 私有 HttpClient：登录换到的东西绝不能进 DI 里那个共享客户端，三站请求靠它带 cookie。
            using var http = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false
            })
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Akashic");

            var (token, exchangeError) =
                await ExchangeAsync(http, clientId, clientSecret, code, redirect, verifier, ct)
                    .ConfigureAwait(false);
            if (token.Length > 0)
            {
                var (userId, login, avatar, userError) =
                    await ReadUserAsync(http, token, ct).ConfigureAwait(false);
                if (userId <= 0)
                    return Fail(userError,
                        "授权已经完成，但 Akashic 没有读到你的 GitHub 账号。", authorizeUrl);

                var (ghOk, ghError) =
                    await _gitHub.LoginWithTokenAsync(token, ct).ConfigureAwait(false);
                if (!ghOk)
                    return Fail("已经拿到令牌，但写入 GitHub CLI 失败",
                        $"{ghError}\n多数是授权范围不够：请重新点一次「使用浏览器登录 GitHub」，" +
                        "或在终端运行 gh auth login -s repo,read:org,gist --web", authorizeUrl);

                return new GitHubLoginResult
                {
                    Ok = true,
                    UserId = userId,
                    Login = login,
                    AvatarUrl = avatar,
                    AuthorizeUrl = authorizeUrl
                };
            }

            return Fail("换取令牌失败", clientSecret.Length > 0
                ? $"{exchangeError}\n若 GitHub 说回调不匹配，请确认这个 App 的 授权回调网址 正是 {RegisteredCallback}。"
                : $"{exchangeError}\n这份构建没有带上 Client Secret，GitHub 的令牌交换会拒绝。", authorizeUrl);
        }
    }

    private string BuildAuthorizeUrl(string clientId, string redirect, string state, string challenge) =>
        string.Join('&',
            $"{_authority}/login/oauth/authorize?client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirect)}",
            $"scope={Uri.EscapeDataString(string.Join(' ', Scopes))}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            "code_challenge_method=S256");

    /// <summary>
    /// Binds 127.0.0.1 only — never <c>localhost</c>, which can resolve to ::1 while GitHub hands back a
    /// v4 redirect, and never a wildcard, so nothing off this machine can be sent the code. The port is
    /// whatever the OS has free, which is why the registered callback leaves it out.
    /// </summary>
    private static (HttpListener listener, int port) OpenLoopback()
    {
        Exception? last = null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var port = FreeTcpPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException ex)
            {
                listener.Close();
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("这台机器上没有可用的回环端口");
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    /// Keeps answering the browser until the callback arrives with our <c>state</c>, the window closes,
    /// or GitHub reports a refusal. A stale request from an earlier attempt must not be able to log
    /// anyone in, so anything else is discarded and the wait continues.
    /// </summary>
    private static async Task<(string code, string error)> WaitForCodeAsync(
        HttpListener listener, string state, TimeSpan window, CancellationToken ct)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(window);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timed.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (string.Empty, ct.IsCancellationRequested
                    ? "登录已取消"
                    : $"等待浏览器授权超时（{Human(window)}）");
            }

            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (!path.StartsWith("/callback", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyAsync(context, false, "这不是 Akashic 的登录回调。", ct).ConfigureAwait(false);
                continue;
            }

            var query = context.Request.QueryString;
            var error = query["error"] ?? string.Empty;

            if (error.Length > 0)
            {
                var reason = error.Equals("access_denied", StringComparison.OrdinalIgnoreCase)
                    ? "你在浏览器里取消了授权"
                    : $"GitHub 返回 {error}";

                await ReplyAsync(context, false, reason, ct).ConfigureAwait(false);
                return (string.Empty, reason);
            }

            if (!string.Equals(query["state"] ?? string.Empty, state, StringComparison.Ordinal))
            {
                await ReplyAsync(context, false, "回调校验不一致，已忽略。请回到 Akashic 重新点一次登录。", ct)
                    .ConfigureAwait(false);
                continue;
            }

            var code = query["code"] ?? string.Empty;
            if (code.Length == 0)
            {
                await ReplyAsync(context, false, "回调里没有授权码。", ct).ConfigureAwait(false);
                continue;
            }

            await ReplyAsync(context, true, string.Empty, ct).ConfigureAwait(false);
            return (code, string.Empty);
        }
    }

    private static async Task ReplyAsync(HttpListenerContext context, bool ok, string detail, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(ok ? SuccessPage : FailurePage(detail));

        try
        {
            context.Response.StatusCode = ok ? 200 : 400;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The browser may close the tab the moment it has the redirect; nothing to report.
        }
        finally
        {
            context.Response.Close();
        }
    }

    private const string SuccessPage =
        """<!doctype html><html lang="zh"><meta charset="utf-8"><title>Akashic</title>""" +
        """<body style="font:15px/1.6 system-ui,sans-serif;background:#0f1115;color:#e8eaf0;text-align:center;padding:64px 24px">""" +
        """<h2 style="margin:0 0 8px">登录完成</h2>""" +
        """<p style="margin:0;color:#9aa3b2">可以回到 Akashic 了，这个标签页会自动关闭。</p>""" +
        """<script>setTimeout(function(){window.close();},1200);</script></html>""";

    private static string FailurePage(string detail) =>
        """<!doctype html><html lang="zh"><meta charset="utf-8"><title>Akashic</title>""" +
        """<body style="font:15px/1.6 system-ui,sans-serif;background:#0f1115;color:#e8eaf0;text-align:center;padding:64px 24px">""" +
        """<h2 style="margin:0 0 8px">登录未完成</h2>""" +
        $"""<p style="margin:0;color:#9aa3b2">{WebUtility.HtmlEncode(detail)}</p>""" +
        "</html>";

    private async Task<(string token, string error)> ExchangeAsync(
        HttpClient http, string clientId, string clientSecret, string code, string redirect,
        string verifier, CancellationToken ct)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("redirect_uri", redirect),
            new("code", code),
            new("code_verifier", verifier)
        };

        if (clientSecret.Length > 0) form.Add(new("client_secret", clientSecret));

        try
        {
            var response = await http
                .PostAsync($"{_authority}/login/oauth/access_token", new FormUrlEncodedContent(form), ct)
                .ConfigureAwait(false);

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (string.Empty, $"HTTP {(int)response.StatusCode}：{Short(text)}");

            using var doc = Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var value) && value.ValueKind == JsonValueKind.String)
                return (value.GetString() ?? string.Empty, string.Empty);

            var reason = Read(root, "error", "没有返回 access_token");
            var description = Read(root, "error_description", string.Empty);

            return (string.Empty, description.Length > 0 ? $"{reason}：{description}" : reason);
        }
        catch (Exception ex)
        {
            return (string.Empty, ex.Message);
        }
    }

    private async Task<(long id, string login, string avatar, string error)> ReadUserAsync(
        HttpClient http, string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiAuthority}/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (0, string.Empty, string.Empty, $"读取账号失败：HTTP {(int)response.StatusCode} {Short(text)}");

            using var doc = Parse(text);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var value) && value.TryGetInt64(out var parsed) ? parsed : 0;

            return id <= 0
                ? (0, string.Empty, string.Empty, "GitHub 没有返回账号 id")
                : (id, Read(root, "login", string.Empty), Read(root, "avatar_url", string.Empty), string.Empty);
        }
        catch (Exception ex)
        {
            return (0, string.Empty, string.Empty, $"读取账号失败：{ex.Message}");
        }
    }

    /// <summary>
    /// GitHub answers JSON when asked for it, but a misconfigured App or an intermediate can hand back
    /// the older urlencoded body. Both are read here instead of trusting the Accept header.
    /// </summary>
    private static JsonDocument Parse(string text)
    {
        var body = text.TrimStart();
        if (body.StartsWith('{')) return JsonDocument.Parse(body);

        var json = new JsonObject();

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;
            json[parts[0]] = JsonValue.Create(Uri.UnescapeDataString(parts[1]));
        }

        return JsonDocument.Parse(json.ToJsonString());
    }

    private static string Read(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Human(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} 分钟" : $"{(int)span.TotalSeconds} 秒";

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static string Short(string text)
    {
        var clean = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return clean.Length <= 300 ? clean : clean[..300] + "…";
    }

    private static GitHubLoginResult Fail(string error, string guidance, string authorizeUrl = "") =>
        new() { Ok = false, Error = error, Guidance = guidance, AuthorizeUrl = authorizeUrl };
}
