using System.Text.Json.Serialization;

namespace Akashic.Host.Preview.Hlsl;

public sealed class ShaderReflection
{
    public List<ShaderBuffer> Buffers { get; set; } = [];

    public List<ShaderResource> Resources { get; set; } = [];

    public List<string> Inputs { get; set; } = [];
}

public sealed class ShaderBuffer
{
    public string Name { get; set; } = string.Empty;

    public uint BindPoint { get; set; }

    public uint Size { get; set; }

    public List<ShaderUniform> Variables { get; set; } = [];
}

public sealed class ShaderUniform
{
    public string Name { get; set; } = string.Empty;

    public uint Offset { get; set; }

    public uint Size { get; set; }

    public string Type { get; set; } = string.Empty;
}

public sealed class ShaderResource
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Dimension { get; set; } = string.Empty;

    public uint BindPoint { get; set; }
}

public sealed class CompiledPass
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Outline { get; set; }

    public ShaderLabPassInfo Pass { get; set; } = new();

    public string VertexCso { get; set; } = string.Empty;

    public string PixelCso { get; set; } = string.Empty;

    public ShaderReflection Vertex { get; set; } = new();

    public ShaderReflection Pixel { get; set; } = new();
}

public sealed class ShaderLabPassInfo
{
    public string Name { get; set; } = string.Empty;

    public string LightMode { get; set; } = string.Empty;

    public bool IsOutline { get; set; }

    public string Cull { get; set; } = "Back";

    public string ZWrite { get; set; } = "On";

    public string ZTest { get; set; } = "LEqual";

    public string SrcBlend { get; set; } = "One";

    public string DstBlend { get; set; } = "Zero";

    public string SrcBlendAlpha { get; set; } = "One";

    public string DstBlendAlpha { get; set; } = "Zero";

    public string BlendOp { get; set; } = "Add";

    public string BlendOpAlpha { get; set; } = "Add";

    public string ColorMask { get; set; } = "15";

    public string OffsetFactor { get; set; } = "0";

    public string OffsetUnits { get; set; } = "0";

    public string AlphaToMask { get; set; } = "0";
}

public sealed class CompiledShader
{
    public string ShaderPath { get; set; } = string.Empty;

    public string ShaderName { get; set; } = string.Empty;

    public string Pipeline { get; set; } = "builtin";

    public bool Fallback { get; set; }

    public string Error { get; set; } = string.Empty;

    public List<CompiledPass> Passes { get; set; } = [];

    public Dictionary<string, float[]> PropertyDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool Ok => Fallback || Passes.Count > 0;
}
