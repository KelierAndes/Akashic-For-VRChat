using Akashic.Core.Models;
using Akashic.Host.Preview.Hlsl;
using Xunit;

namespace Akashic.Tests;

public sealed class TextureCacheTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public async Task ReplacingTheSessionEvictsTexturesItNoLongerUses()
    {
        var compiler = new ShaderCompileService(Path.Combine(AppContext.BaseDirectory, "cache", "preview"));
        Assert.True(compiler.ToolError is null, compiler.ToolError);

        var textured = new PreviewMaterial
        {
            Name = "Cached",
            Path = Path.Combine(FixtureRoot, "materials", "golden.mat"),
            Shader = Path.Combine(FixtureRoot, "shaders", "golden.shader"),
            BaseTexture = Path.Combine(FixtureRoot, "textures", "golden-checker.png"),
            Kind = "unlit",
            AlphaMode = "opaque"
        };

        var plain = new PreviewMaterial
        {
            Name = "Plain",
            Path = Path.Combine(FixtureRoot, "materials", "standard.mat"),
            BuiltInShader = "Standard",
            ShaderFamily = "Standard",
            Kind = "lit",
            AlphaMode = "opaque"
        };

        var sceneA = HlslSceneBuilder.BuildMaterialPreview("cache-a", textured);
        var sceneB = HlslSceneBuilder.BuildMaterialPreview("cache-b", plain);
        var shaderA = await compiler.GetAsync(textured.Shader, sceneA.Root, [], false, [], "gamma",
            CancellationToken.None);
        var standardPath = compiler.ResolveBuiltIn("Standard") ?? throw new InvalidOperationException("缺内置 Standard");
        var shaderB = await compiler.GetAsync(standardPath, sceneB.Root, [], false, [], "gamma",
            CancellationToken.None);
        var fallback = await compiler.GetFallbackAsync(false, CancellationToken.None);

        Assert.True(shaderA.Ok && shaderB.Ok);

        using var renderer = D3D11PreviewRenderer.Create(softwareOnly: true, textureCap: 1);
        Assert.True(renderer.Render(sceneA, Map(textured, shaderA), fallback, Options("cache-a"))
            .TextureBytes > 0);
        Assert.True(renderer.TextureCacheBytes > 0);

        renderer.Render(sceneB, Map(plain, shaderB), fallback, Options("cache-b"));
        Assert.Equal(0, renderer.TextureCacheBytes);
    }

    private static Dictionary<PreviewMaterial, CompiledShader> Map(PreviewMaterial material, CompiledShader shader) =>
        new() { [material] = shader };

    private static RenderOptions Options(string session) => new()
    {
        SessionId = session,
        Width = 128,
        Height = 128,
        View = "iso",
        Mode = "textured",
        Outline = false,
        Gizmo = false
    };
}
