using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akashic.Core.Services;

public static class GitHubOAuthApp
{
    private const string Pepper = "Akashic.OAuthApp.v1:7f2c9be41a";
    private const string Prefix = "Akashic.OAuthApp.v1:";
    private static readonly byte[] Salt = "Akashic.OAuthApp.Salt.v1"u8.ToArray();
    private const int Iterations = 100_000;

    private const string BuiltIn =
        "/8w2DFggy7cU/1FgoBoGbk00ytxl3cVmMNp19h2NWKKUhjio/Nlea4aefwjI1M/aXocjKFuc6uKzkmf5cQlv2sEFpltXxL+lv9tl8HQ1DuRSaYDiK9+TmBJo/RZX/wYH67zGs04H8i53Zeo4k/Xlfdt81GOuK1BSaw==";

    public const string CallbackUrl = "http://127.0.0.1/callback/";

    public static (string clientId, string clientSecret) Resolve()
    {
        var overrideId = (Environment.GetEnvironmentVariable("AKASHIC_GITHUB_CLIENT_ID") ?? string.Empty).Trim();
        if (overrideId.Length > 0)
            return (overrideId,
                (Environment.GetEnvironmentVariable("AKASHIC_GITHUB_CLIENT_SECRET") ?? string.Empty).Trim());

        return Unprotect(BuiltIn);
    }

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
