using Akashic.Core.Services.Preview.Shader;
using Xunit;

namespace Akashic.Tests;

public class ShaderLabExtractorTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "shaders", name);

    [Fact]
    public void ParsesNamePropertiesAndPasses()
    {
        var program = ShaderLabExtractor.Parse(Fixture("basic.shader"));

        Assert.True(program.SupportsHlsl);
        Assert.Equal("Akashic/Fixture/Basic", program.Name);
        Assert.Equal("builtin", program.Pipeline);
        Assert.Equal(2, program.Passes.Count);
        Assert.Equal("ForwardBase", program.Passes[0].LightMode);
        Assert.Equal("ShadowCaster", program.Passes[1].LightMode);
        Assert.Contains("_EMISSION", program.Passes[0].DefaultKeywords);

        Assert.Equal([1f, 1f, 1f, 1f], program.Properties.Single(item => item.Name == "_Color").Numbers);
        Assert.Empty(program.Properties.Single(item => item.Name == "_MainTex").Numbers);
        Assert.Equal([0.5f], program.Properties.Single(item => item.Name == "_Glossiness").Numbers);
    }

    [Fact]
    public void PassSourceCarriesShimAndCommentsPragmas()
    {
        var program = ShaderLabExtractor.Parse(Fixture("basic.shader"));
        var source = program.Passes[0].Source;

        Assert.StartsWith("#include \"UnityShim.hlsl\"", source);
        Assert.Contains("// #pragma vertex vert", source);
        Assert.Contains("UnityObjectToClipPos", source);
    }

    [Fact]
    public void UrpShaderIsUnsupported()
    {
        var program = ShaderLabExtractor.Parse(Fixture("urp.shader"));

        Assert.False(program.SupportsHlsl);
        Assert.Equal("urp", program.Pipeline);
        Assert.Contains("URP", program.UnsupportedReason);
    }

    [Fact]
    public void SurfaceShaderIsUnsupported()
    {
        var program = ShaderLabExtractor.Parse(Fixture("surface.shader"));

        Assert.False(program.SupportsHlsl);
        Assert.Contains("Surface Shader", program.UnsupportedReason);
    }

    [Fact]
    public void OutlinePassIsMarked()
    {
        var program = ShaderLabExtractor.Parse(Fixture("outline.shader"));

        Assert.Single(program.Passes);
        Assert.True(program.Passes[0].IsOutline);
        Assert.Equal("Front", program.Passes[0].Cull);
    }

    [Fact]
    public void OversizedSourceIsRejectedWithoutParsing()
    {
        var directory = Directory.CreateTempSubdirectory("akashic-shader-");
        try
        {
            var path = Path.Combine(directory.FullName, "huge.shader");
            File.WriteAllText(path, "Shader \"Akashic/Huge\" {" +
                                    new string(' ', ShaderLabExtractor.MaxShaderBytes + 4096) + "}");

            var program = ShaderLabExtractor.Parse(path);

            Assert.False(program.SupportsHlsl);
            Assert.Contains("过大", program.UnsupportedReason);
            Assert.Equal("size", program.FailureKind);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
