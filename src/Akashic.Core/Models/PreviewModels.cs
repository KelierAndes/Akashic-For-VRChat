namespace Akashic.Core.Models;

public class PreviewPrefabInfo
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public string Kind { get; set; } = "prefab";

    public long Size { get; set; }
}

public class PreviewRequest
{
    public string ResourceKey { get; set; } = string.Empty;

    public string PrefabPath { get; set; } = string.Empty;

    public string? Entry { get; set; }
}

public class MaterialRenderRequest
{
    public string ResourceKey { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? Entry { get; set; }

    public int Width { get; set; } = 512;

    public int Height { get; set; } = 512;
}

public class PreviewScene
{
    public string PrefabPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Root { get; set; } = string.Empty;

    public List<PreviewNode> Nodes { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public int MeshCount { get; set; }

    public int MaterialCount { get; set; }

    public int NodeCount { get; set; }
}

public class PreviewNode
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public float[] Position { get; set; } = [0, 0, 0];

    public float[] Rotation { get; set; } = [0, 0, 0, 1];

    public float[] Scale { get; set; } = [1, 1, 1];

    public PreviewMesh? Mesh { get; set; }

    public List<PreviewMaterial> Materials { get; set; } = [];

    public List<PreviewComponent> Components { get; set; } = [];

    public List<string> FileIds { get; set; } = [];

    public List<PreviewNode> Children { get; set; } = [];
}

public class PreviewMesh
{
    public string AssetPath { get; set; } = string.Empty;

    public string SubMesh { get; set; } = string.Empty;

    public long FileId { get; set; }

    public bool Skinned { get; set; }
}

public class PreviewMaterial
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Shader { get; set; } = string.Empty;

    public string ShaderName { get; set; } = string.Empty;

    public string ShaderFamily { get; set; } = string.Empty;

    public string Pipeline { get; set; } = "builtin";

    public string Kind { get; set; } = "lit";

    public bool Procedural { get; set; }

    public bool DoubleSided { get; set; }

    public string AlphaMode { get; set; } = "opaque";

    public float Cutoff { get; set; } = 0.5f;

    public float[] BaseColor { get; set; } = [1, 1, 1, 1];

    public string BaseTexture { get; set; } = string.Empty;

    public Dictionary<string, string> Textures { get; set; } = [];

    public Dictionary<string, float[]> TextureST { get; set; } = [];

    public List<string> Keywords { get; set; } = [];

    public List<string> Slots { get; set; } = [];

    public Dictionary<string, float[]> Colors { get; set; } = [];

    public Dictionary<string, float> Floats { get; set; } = [];

    public float[] ShadeColor1 { get; set; } = [0.8f, 0.8f, 0.8f, 1];

    public float[] ShadeColor2 { get; set; } = [0.6f, 0.6f, 0.6f, 1];

    public float ShadowStrength { get; set; } = 1;

    public float LightMinLimit { get; set; } = 0.05f;

    public float LightMaxLimit { get; set; } = 1;

    public bool AsUnlit { get; set; }

    public bool MonochromeLighting { get; set; }

    public int TransparentMode { get; set; }

    public bool UseCutout { get; set; }

    public bool UseEmission { get; set; }

    public float[] EmissionColor { get; set; } = [0, 0, 0, 1];

    public string EmissionTexture { get; set; } = string.Empty;

    public bool UseMatCap { get; set; }

    public float[] MatCapColor { get; set; } = [1, 1, 1, 1];

    public string MatCapTexture { get; set; } = string.Empty;

    public bool UseOutline { get; set; }

    public float[] OutlineColor { get; set; } = [0, 0, 0, 1];

    public float OutlineWidth { get; set; }

    public string NormalTexture { get; set; } = string.Empty;

    public float NormalScale { get; set; } = 1;

    public bool PremultipliedAlpha { get; set; }
}

public class PreviewComponent
{
    public int ClassId { get; set; }

    public string Type { get; set; } = string.Empty;

    public long FileId { get; set; }

    public string Script { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Dictionary<string, object?> Properties { get; set; } = [];
}

public class PreviewBuildResult
{
    public PreviewScene Scene { get; set; } = new();

    public string SessionId { get; set; } = string.Empty;

    public string ViewerUrl { get; set; } = string.Empty;

    public string ModelUrl { get; set; } = string.Empty;

    public long ModelBytes { get; set; }

    public List<string> Warnings { get; set; } = [];
}

public class PreviewBuildArtifacts
{
    public string SessionId { get; set; } = string.Empty;

    public string Directory { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;

    public PreviewScene Scene { get; set; } = new();
}
