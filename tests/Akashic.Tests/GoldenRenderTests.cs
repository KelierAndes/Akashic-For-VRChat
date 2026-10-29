using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Akashic.Core.Models;
using Akashic.Host.Preview.Hlsl;
using Xunit;

namespace Akashic.Tests;

public sealed class GoldenRenderTests
{
    private const int Size = 256;
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly string GoldenPath = Path.Combine(RepoRoot(), "docs", "preview-golden.json");
    private static readonly string UpdateFlag = "AKASHIC_GOLDEN_UPDATE";

    private static readonly int[][] CheckerColors =
    [
        [232, 48, 48],
        [48, 200, 72],
        [56, 96, 232],
        [240, 240, 240]
    ];

    [Fact]
    public async Task UnlitTexturedSphere_MatchesGoldenPixels()
    {
        var material = new PreviewMaterial
        {
            Name = "GoldenUnlit",
            Path = Path.Combine(FixtureRoot, "materials", "golden.mat"),
            Shader = Path.Combine(FixtureRoot, "shaders", "golden.shader"),
            BaseTexture = Path.Combine(FixtureRoot, "textures", "golden-checker.png"),
            Kind = "unlit",
            AlphaMode = "opaque"
        };

        var frame = await RenderAsync(material, "unlit-textured");

        Assert.Equal(0, frame.Fallbacks);
        Assert.Equal(1, frame.Textures);
        Assert.Equal(0, frame.TextureDecodeFailures);

        var coverage = Coverage(frame);
        Assert.True(coverage is > 0.15 and < 0.95, $"非背景像素占比 {coverage:F3} 不在预期区间");

        for (var i = 0; i < CheckerColors.Length; i++)
        {
            var count = CountNear(frame, CheckerColors[i], 12);
            Assert.True(count > 100, $"贴图色块 {i} 只有 {count} 个像素，说明 UV 或采样链路变了");
        }
    }

    [Fact]
    public async Task BuiltInStandardSphere_MatchesGoldenPixels()
    {
        var material = new PreviewMaterial
        {
            Name = "GoldenStandard",
            Path = Path.Combine(FixtureRoot, "materials", "standard.mat"),
            BuiltInShader = "Standard",
            ShaderFamily = "Standard",
            Kind = "lit",
            AlphaMode = "opaque"
        };

        var frame = await RenderAsync(material, "builtin-standard");

        Assert.Equal(0, frame.Fallbacks);
        var coverage = Coverage(frame);
        Assert.True(coverage is > 0.15 and < 0.95, $"非背景像素占比 {coverage:F3} 不在预期区间");
    }

    [Fact]
    public async Task ManualVariant_ChangesPixelsAndKeepsTheUntouchedPathIdentical()
    {
        var off = await RenderAsync(VariantMaterial(), "variant-off");
        var on = await RenderAsync(VariantMaterial(), "variant-on", ["_AKASHIC_INVERT_ON"]);
        var linear = await RenderAsync(VariantMaterial(), "variant-linear", colorSpace: "linear");

        Assert.Equal(0, on.Fallbacks);
        Assert.NotEqual(Hash(off), Hash(on));
        Assert.NotEqual(Hash(off), Hash(linear));

        var untouched = await RenderAsync(VariantMaterial(), "variant-off");
        Assert.Equal(Hash(off), Hash(untouched));
    }

    private static PreviewMaterial VariantMaterial() => new()
    {
        Name = "GoldenVariant",
        Path = Path.Combine(FixtureRoot, "materials", "variant.mat"),
        Shader = Path.Combine(FixtureRoot, "shaders", "variant.shader"),
        BaseTexture = Path.Combine(FixtureRoot, "textures", "golden-checker.png"),
        Kind = "unlit",
        AlphaMode = "opaque"
    };

    private static async Task<RenderFrame> RenderAsync(PreviewMaterial material, string goldenKey,
        IReadOnlyList<string>? extraKeywords = null, string colorSpace = "gamma")
    {
        var cacheRoot = Path.Combine(AppContext.BaseDirectory, "cache", "preview");
        var compiler = new ShaderCompileService(cacheRoot);
        Assert.True(compiler.ToolError is null, compiler.ToolError);

        var shaderPath = material.Shader;
        if (!File.Exists(shaderPath))
        {
            shaderPath = compiler.ResolveBuiltIn(material.BuiltInShader)
                         ?? throw new InvalidOperationException("内置着色器解析失败。");
        }

        var scene = HlslSceneBuilder.BuildMaterialPreview(goldenKey, material);
        var compiled = await compiler.GetAsync(shaderPath, scene.Root, material.Keywords, false,
            extraKeywords ?? [], colorSpace, CancellationToken.None);
        Assert.True(compiled.Ok, compiled.Error);

        using var renderer = D3D11PreviewRenderer.Create(softwareOnly: true);
        Assert.Equal("WARP", renderer.Adapter);

        var fallback = await compiler.GetFallbackAsync(false, CancellationToken.None);
        var frame = renderer.Render(scene,
            new Dictionary<PreviewMaterial, CompiledShader> { [material] = compiled }, fallback,
            new RenderOptions
            {
                SessionId = goldenKey, Width = Size, Height = Size, View = "iso", Mode = "textured",
                Outline = false, Gizmo = false, Raw = true,
                Keywords = [.. extraKeywords ?? []], ColorSpace = colorSpace
            });

        Assert.Equal(Size * Size * 4, frame.Pixels.Length);
        AssertGolden(frame, goldenKey);
        return frame;
    }

    private static string Hash(RenderFrame frame) =>
        Convert.ToHexString(SHA256.HashData(frame.Pixels)).ToLowerInvariant();

    private static void AssertGolden(RenderFrame frame, string key)
    {
        var hash = Hash(frame);
        var document = File.Exists(GoldenPath)
            ? JsonNode.Parse(File.ReadAllText(GoldenPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();

        if (Environment.GetEnvironmentVariable(UpdateFlag) == "1")
        {
            document[key] = hash;
            document["note"] = "WARP 软件渲染、256x256、iso 正交视图、原始 RGBA 的 SHA256。" +
                               "改动渲染器后用 $env:AKASHIC_GOLDEN_UPDATE=1 跑 dotnet test 重新生成，再逐项确认差异。";
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)!);
            File.WriteAllText(GoldenPath, document.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return;
        }

        Assert.True(document.TryGetPropertyValue(key, out var expected),
            $"黄金图 {key} 还没有基线，设置 {UpdateFlag}=1 运行一次以生成 docs/preview-golden.json");
        Assert.Equal(expected?.GetValue<string>(), hash);
    }

    private static double Coverage(RenderFrame frame)
    {
        var pixels = frame.Pixels;
        var lit = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var delta = Math.Abs(pixels[i] - 19) + Math.Abs(pixels[i + 1] - 21) + Math.Abs(pixels[i + 2] - 24);
            if (delta > 24) lit++;
        }

        return (double)lit / (frame.Width * frame.Height);
    }

    private static int CountNear(RenderFrame frame, int[] color, int tolerance)
    {
        var pixels = frame.Pixels;
        var count = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i] - color[0]) <= tolerance &&
                Math.Abs(pixels[i + 1] - color[1]) <= tolerance &&
                Math.Abs(pixels[i + 2] - color[2]) <= tolerance) count++;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               (!Directory.Exists(Path.Combine(directory.FullName, "docs")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "tests"))))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
