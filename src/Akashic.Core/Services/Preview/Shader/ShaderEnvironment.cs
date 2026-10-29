using System.Reflection;

namespace Akashic.Core.Services.Preview.Shader;

public static class ShaderEnvironment
{
    public const string Version = "shim-v8";

    private const string Prefix = "Akashic.Core.Services.Preview.Shader.Resources.";

    private static readonly object Gate = new();

    public static string StandardShaderPath(string cacheRoot) =>
        Path.Combine(Ensure(cacheRoot), "Standard.shader");

    public static string UiShaderPath(string cacheRoot) =>
        Path.Combine(Ensure(cacheRoot), "UIDefault.shader");

    public static string Ensure(string cacheRoot)
    {
        lock (Gate)
        {
            var root = PreviewCachePaths.VersionedUnder(cacheRoot, "hlsl", Version);
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
}
