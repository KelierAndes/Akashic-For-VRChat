using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akashic.Core.Services;

/// <summary>
/// The GitHub OAuth App Akashic ships with. The pair is registered once by the project owner and
/// stored as an AES-GCM blob inside the assembly, so a user never sees it, never configures it, and
/// never has to leave the settings page to start a sync.
///
/// Honest about what the encryption buys: the pepper that derives the key sits in the same binary,
/// so this conceals the pair from a casual reader of the DLL or the source — it does not keep it from
/// someone willing to reverse a release. That is unavoidable for a native client that talks to an
/// OAuth App, and the real limit on abuse is the registered callback URL, which pins where a token
/// may be sent. Rotate the secret at GitHub if it ever matters.
///
/// Development override: <c>AKASHIC_GITHUB_CLIENT_ID</c> and <c>AKASHIC_GITHUB_CLIENT_SECRET</c>
/// replace the built-in pair, so a second OAuth App can be tested without rebuilding.
/// </summary>
public static class GitHubOAuthApp
{
    private const string Pepper = "Akashic.OAuthApp.v1:7f2c9be41a";
    private const string Prefix = "Akashic.OAuthApp.v1:";
    private static readonly byte[] Salt = "Akashic.OAuthApp.Salt.v1"u8.ToArray();
    private const int Iterations = 100_000;

    /// <summary>
    /// The blob to ship. Regenerated with <see cref="Protect"/> whenever the App is edited at
    /// GitHub; the plaintext never belongs in the repository.
    /// </summary>
    private const string BuiltIn =
        "/8w2DFggy7cU/1FgoBoGbk00ytxl3cVmMNp19h2NWKKUhjio/Nlea4aefwjI1M/aXocjKFuc6uKzkmf5cQlv2sEFpltXxL+lv9tl8HQ1DuRSaYDiK9+TmBJo/RZX/wYH67zGs04H8i53Zeo4k/Xlfdt81GOuK1BSaw==";

    /// <summary>What the App's 授权回调网址 has to be set to. No port: GitHub lets the port vary.</summary>
    public const string CallbackUrl = "http://127.0.0.1/callback/";

    public static (string clientId, string clientSecret) Resolve()
    {
        var overrideId = (Environment.GetEnvironmentVariable("AKASHIC_GITHUB_CLIENT_ID") ?? string.Empty).Trim();
        if (overrideId.Length > 0)
            return (overrideId,
                (Environment.GetEnvironmentVariable("AKASHIC_GITHUB_CLIENT_SECRET") ?? string.Empty).Trim());

        return Unprotect(BuiltIn);
    }

    /// <summary>
    /// Build-time twin of <see cref="Resolve"/>: wraps a JSON pair into the blob to paste into
    /// <see cref="BuiltIn"/>. Nothing at runtime calls it.
    /// </summary>
    public static string Protect(string plainJson)
    {
        var plain = Encoding.UTF8.GetBytes(plainJson);
        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        var key = Key();
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(iv, plain, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var blob = new byte[iv.Length + cipher.Length + tag.Length];
        iv.CopyTo(blob.AsSpan());
        cipher.CopyTo(blob.AsSpan(iv.Length));
        tag.CopyTo(blob.AsSpan(iv.Length + cipher.Length));

        return Convert.ToBase64String(blob);
    }

    private static (string clientId, string clientSecret) Unprotect(string blob)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blob)) return (string.Empty, string.Empty);

            var raw = Convert.FromBase64String(blob.Trim());
            if (raw.Length <= 28) return (string.Empty, string.Empty);

            var plain = new byte[raw.Length - 28];
            var key = Key();
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(raw.AsSpan(0, 12),
                    raw.AsSpan(12, plain.Length),
                    raw.AsSpan(raw.Length - 16, 16),
                    plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(plain));
            return (Text(doc, "ClientId"), Text(doc, "ClientSecret"));
        }
        catch (Exception)
        {
            // A tampered or mismatched blob is the same as having none: the login page says so.
            return (string.Empty, string.Empty);
        }
    }

    private static byte[] Key() => Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(Prefix + Pepper), Salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static string Text(JsonDocument doc, string name) =>
        doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
