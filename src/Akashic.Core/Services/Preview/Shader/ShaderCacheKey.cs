using System.Security.Cryptography;
using System.Text;

namespace Akashic.Core.Services.Preview.Shader;

public static class ShaderCacheKey
{
    public static string For(string environmentVersion, string shaderPath, long stampTicks, string keywordKey,
        bool outline) =>
        Hash($"{environmentVersion}|{shaderPath}|{stampTicks}|{keywordKey}|{outline}");

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..24].ToLowerInvariant();
}
