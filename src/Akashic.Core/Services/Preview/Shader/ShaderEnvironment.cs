using System.Reflection;

namespace Akashic.Core.Services.Preview.Shader;

public static class ShaderEnvironment
{
    public const string Version = "shim-v5";

    private const string Prefix = "Akashic.Core.Services.Preview.Shader.Resources.";

    public static string Ensure(string cacheRoot)
    {
        var root = Path.Combine(cacheRoot, "hlsl", Version);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "UnityStubs"));

        var assembly = typeof(ShaderEnvironment).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;

            var relative = name[Prefix.Length..];
            string target;
            if (relative.StartsWith("UnityStubs.", StringComparison.Ordinal))
                target = Path.Combine(root, "UnityStubs", relative["UnityStubs.".Length..]);
            else
                target = Path.Combine(root, relative);

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (File.Exists(target) && new FileInfo(target).Length == bytes.Length &&
                File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                continue;

            File.WriteAllBytes(target, bytes);
        }

        return root;
    }
}
