using System.Numerics;
using System.Runtime.InteropServices;
using Akashic.Core.Models;
using StbImageSharp;
using StbImageWriteSharp;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Blend = Vortice.Direct3D11.Blend;
using BlendOperation = Vortice.Direct3D11.BlendOperation;
using Format = Vortice.DXGI.Format;

namespace Akashic.Host.Preview.Hlsl;

public sealed class RenderOptions
{
    public string SessionId { get; set; } = string.Empty;

    public int Width { get; set; } = 900;

    public int Height { get; set; } = 900;

    public float Yaw { get; set; } = 0.65f;

    public float Pitch { get; set; } = 0.22f;

    public float Distance { get; set; }

    public float Fov { get; set; } = 30;

    public bool Outline { get; set; } = true;

    public string View { get; set; } = "quad";

    public string Mode { get; set; } = "textured";

    public bool Gizmo { get; set; }

    public bool LightBackground { get; set; }

    public float Time { get; set; }
}

public sealed class RenderFrame
{
    public byte[] Jpeg { get; set; } = [];

    public int DrawCalls { get; set; }

    public int Materials { get; set; }

    public int Fallbacks { get; set; }

    public List<string> FallbackNotes { get; set; } = [];

    public Dictionary<string, int> FailureKinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Warning { get; set; }
}

public sealed class RendererStatus
{
    public bool Available { get; set; }

    public string Error { get; set; } = string.Empty;

    public string Adapter { get; set; } = string.Empty;

    public string FeatureLevel { get; set; } = string.Empty;
}

public sealed class D3D11PreviewRenderer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Buffer _engine;
    private readonly ID3D11SamplerState _linearWrap;
    private readonly ID3D11SamplerState _linearClamp;
    private readonly ID3D11Texture2D _defaultWhite;
    private readonly ID3D11Texture2D _defaultNormal;
    private readonly ID3D11Texture2D _defaultBlack;
    private readonly ID3D11ShaderResourceView _whiteView;
    private readonly ID3D11ShaderResourceView _normalView;
    private readonly ID3D11ShaderResourceView _blackView;
    private readonly Dictionary<string, ID3D11ShaderResourceView> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ID3D11BlendState> _blendCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ID3D11RasterizerState> _rasterCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ID3D11DepthStencilState> _depthCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GpuPass> _gpuPasses = new(StringComparer.Ordinal);

    private GpuSession? _session;
    private ID3D11Texture2D? _color;
    private ID3D11RenderTargetView? _colorView;
    private ID3D11Texture2D? _depth;
    private ID3D11DepthStencilView? _depthView;
    private ID3D11Texture2D? _staging;
    private int _width;
    private int _height;
    private string _sessionKey = string.Empty;
    private bool _inverseWarned;
    private readonly DateTime _start = DateTime.UtcNow;

    private D3D11PreviewRenderer(ID3D11Device device, string adapter)
    {
        _device = device;
        _context = device.ImmediateContext;
        Adapter = adapter;

        _engine = CreateDynamicBuffer(880);
        _linearWrap = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear,
            TextureAddressMode.Wrap, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _linearClamp = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear,
            TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));

        _defaultWhite = CreateTexture([255, 255, 255, 255], out _whiteView);
        _defaultNormal = CreateTexture([128, 128, 255, 255], out _normalView);
        _defaultBlack = CreateTexture([0, 0, 0, 255], out _blackView);
    }

    public string Adapter { get; }

    public static RendererStatus Probe()
    {
        try
        {
            using var renderer = Create();
            return new RendererStatus
            {
                Available = true,
                Adapter = renderer.Adapter,
                FeatureLevel = "11_0"
            };
        }
        catch (Exception ex)
        {
            return new RendererStatus { Available = false, Error = ex.Message };
        }
    }

    public static D3D11PreviewRenderer Create()
    {
        try
        {
            var device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0]);
            return new D3D11PreviewRenderer(device, "Hardware");
        }
        catch (Exception)
        {
            var device = D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0]);
            return new D3D11PreviewRenderer(device, "WARP");
        }
    }

    public RenderFrame Render(HlslSceneData scene, IReadOnlyDictionary<PreviewMaterial, CompiledShader> shaders,
        CompiledShader fallbackShader, RenderOptions options)
    {
        var width = Math.Clamp(options.Width, 128, 4096);
        var height = Math.Clamp(options.Height, 128, 4096);
        EnsureTargets(width, height);

        var mode = (options.Mode ?? "textured").ToLowerInvariant();
        var debug = mode is "clay" or "wireframe";
        var wireframe = mode == "wireframe";
        var key = scene.SessionId + "|" + options.Outline + "|" + mode + "|" + options.Gizmo + "|" +
                  options.LightBackground + "|" + ShaderSignature(scene, shaders, fallbackShader);
        if (!string.Equals(_sessionKey, key, StringComparison.Ordinal) || _session is null)
        {
            _session?.Dispose();
            _session = BuildSession(scene, shaders, fallbackShader, options.Outline, debug, wireframe);
            _sessionKey = key;
        }

        _context.OMSetRenderTargets(_colorView!, _depthView);
        _context.ClearRenderTargetView(_colorView!, options.LightBackground
            ? new Color4(0.93f, 0.94f, 0.96f, 1f)
            : new Color4(0.075f, 0.082f, 0.094f, 1f));
        var ticks = (float)(DateTime.UtcNow - _start).TotalSeconds;
        var drawCalls = 0;

        if (string.Equals(options.View, "quad", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(options.View))
        {
            var gap = Math.Max(2, Math.Min(width, height) / 160);
            var paneWidth = (width - gap) / 2;
            var paneHeight = (height - gap) / 2;
            var specs = new[] { Views.Front, Views.Left, Views.Top, Views.Iso };
            var paneAspect = (float)paneWidth / Math.Max(1, paneHeight);
            var sharedHeight = specs.Max(spec => FitView(scene, spec, paneAspect).halfHeight);

            for (var i = 0; i < specs.Length; i++)
            {
                var x = i % 2 == 0 ? 0 : paneWidth + gap;
                var y = i < 2 ? 0 : paneHeight + gap;
                _context.RSSetViewport(new Viewport(x, y, paneWidth, paneHeight, 0, 1));
                _context.ClearDepthStencilView(_depthView!, DepthStencilClearFlags.Depth, 1f, 0);
                drawCalls += DrawView(scene, specs[i], paneWidth, paneHeight, ticks, sharedHeight);
            }
        }
        else
        {
            var spec = Views.Find(options.View);
            _context.RSSetViewport(new Viewport(0, 0, width, height, 0, 1));
            _context.ClearDepthStencilView(_depthView!, DepthStencilClearFlags.Depth, 1f, 0);
            drawCalls += DrawView(scene, spec, width, height, ticks);
        }

        _context.Flush();
        var jpeg = ReadBack(width, height);

        return new RenderFrame
        {
            Jpeg = jpeg,
            DrawCalls = drawCalls,
            Materials = _session.Materials,
            Fallbacks = _session.Fallbacks
        };
    }

    private static string ShaderSignature(HlslSceneData scene,
        IReadOnlyDictionary<PreviewMaterial, CompiledShader> shaders, CompiledShader fallback)
    {
        var parts = new List<string>(scene.Materials.Count);
        foreach (var material in scene.Materials)
        {
            var shader = material is not null && shaders.TryGetValue(material, out var found) ? found : fallback;
            parts.Add((material?.Path ?? material?.Name ?? "$default") + "=" + shader.ShaderPath +
                      "#" + (shader.Ok ? "ok" : "bad"));
        }

        return string.Join(";", parts);
    }

    private int DrawView(HlslSceneData scene, ViewSpec spec, int width, int height, float ticks,
        float? sharedHalfHeight = null)
    {
        var camera = CreateCamera(scene, spec, (float)width / height, sharedHalfHeight);
        var items = _session!.Items
            .OrderBy(item => item.Source.Transparent ? 1 : 0)
            .ThenBy(item => item.Source.Transparent
                ? -Vector3.Distance(camera.eye, item.Source.Center)
                : Vector3.Distance(camera.eye, item.Source.Center))
            .ToList();

        var drawCalls = 0;
        foreach (var item in items)
        {
            var world = item.Source.World;
            if (!Matrix4x4.Invert(world, out var inverse))
            {
                inverse = Matrix4x4.Identity;
                if (!_inverseWarned)
                {
                    _inverseWarned = true;
                    Console.WriteLine($"HLSL singular world matrix on {item.Source.Name}, det={world.GetDeterminant()}");
                }
            }
            var engineBytes = BuildEngine(world, inverse, camera, width, height, ticks);
            Upload(_engine, engineBytes);
            _context.VSSetConstantBuffer(0, _engine);
            _context.PSSetConstantBuffer(0, _engine);

            foreach (var binding in item.Material.Passes)
            {
                var vsGlobals = binding.Gpu.VsGlobals;
                var psGlobals = binding.Gpu.PsGlobals;
                if (binding.Gpu.Vs is null || binding.Gpu.Ps is null || vsGlobals is null || psGlobals is null)
                    continue;

                _context.IASetInputLayout(binding.Gpu.Layout);
                _context.IASetVertexBuffer(0, item.VertexBuffer, HlslVertexLayout.Stride, 0);
                _context.IASetIndexBuffer(item.IndexBuffer, Format.R32_UInt, 0);
                _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _context.VSSetShader(binding.Gpu.Vs);
                _context.PSSetShader(binding.Gpu.Ps);
                if (binding.Gpu.VsGlobalsBind != uint.MaxValue)
                {
                    _context.VSSetConstantBuffer(binding.Gpu.VsGlobalsBind, vsGlobals);
                    Upload(vsGlobals, binding.VsGlobals);
                }

                if (binding.Gpu.PsGlobalsBind != uint.MaxValue)
                {
                    _context.PSSetConstantBuffer(binding.Gpu.PsGlobalsBind, psGlobals);
                    Upload(psGlobals, binding.PsGlobals);
                }

                foreach (var texture in binding.Textures)
                {
                    if (texture.Stage) _context.VSSetShaderResource(texture.Slot, texture.View);
                    else _context.PSSetShaderResource(texture.Slot, texture.View);
                }

                foreach (var sampler in binding.Samplers)
                {
                    var state = sampler.Clamp ? _linearClamp : _linearWrap;
                    if (sampler.Stage) _context.VSSetSampler(sampler.Slot, state);
                    else _context.PSSetSampler(sampler.Slot, state);
                }

                _context.RSSetState(binding.Raster);
                _context.OMSetDepthStencilState(binding.Depth, 0);
                _context.OMSetBlendState(binding.Blend);
                _context.DrawIndexed((uint)item.Source.Indices.Length, 0, 0);
                drawCalls++;
            }
        }

        return drawCalls;
    }

    private GpuSession BuildSession(HlslSceneData scene,
        IReadOnlyDictionary<PreviewMaterial, CompiledShader> shaders, CompiledShader fallbackShader, bool outline,
        bool debug, bool wireframe)
    {
        var session = new GpuSession();
        var materials = new Dictionary<PreviewMaterial, GpuMaterial>();
        GpuMaterial? nullMaterial = null;

        GpuMaterial MaterialFor(PreviewMaterial? material)
        {
            if (material is null) return nullMaterial ??= Create(material);
            if (materials.TryGetValue(material, out var cached)) return cached;

            var created = Create(material);
            materials[material] = created;
            return created;

            GpuMaterial Create(PreviewMaterial? source)
            {
                CompiledShader? shader = null;
                if (source is not null) shaders.TryGetValue(source, out shader);

                var usedFallback = shader is null || !shader.Ok || shader.Fallback;
                if (usedFallback) shader = fallbackShader;

                var gpu = new GpuMaterial();
                foreach (var pass in shader?.Passes ?? [])
                {
                    if (pass.Outline && (debug ||
                                         (source is not null && !source.UseOutline && source.OutlineWidth <= 0.0001f)))
                        continue;

                    var gpuPass = GetGpuPass(pass);
                    if (gpuPass is null) continue;

                    var state = debug
                        ? DebugState(source, wireframe)
                        : usedFallback
                            ? FallbackState(source)
                            : ResolveState(pass.Pass, source, shader!);
                    gpu.Passes.Add(new PassBinding
                    {
                        Gpu = gpuPass,
                        VsGlobals = BuildGlobals(pass.Vertex, shader!, source),
                        PsGlobals = BuildGlobals(pass.Pixel, shader!, source),
                        Textures = ResolveTextures(pass, source),
                        Samplers = ResolveSamplers(pass),
                        Raster = Raster(state),
                        Depth = Depth(state),
                        Blend = BlendState(state)
                    });
                }

                if (usedFallback && !debug) session.Fallbacks++;
                session.Materials++;
                return gpu;
            }
        }

        foreach (var item in scene.Items)
        {
            var material = MaterialFor(item.Material);
            if (material.Passes.Count == 0) continue;

            var vertexBuffer = _device.CreateBuffer(item.Vertices,
                new BufferDescription((uint)item.Vertices.Length, BindFlags.VertexBuffer,
                    ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
            var indexBuffer = _device.CreateBuffer(item.Indices,
                new BufferDescription((uint)(item.Indices.Length * 4), BindFlags.IndexBuffer,
                    ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.None, 0));

            session.Items.Add(new GpuDraw
            {
                Source = item,
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                Material = material
            });
        }

        return session;
    }

    private GpuPass? GetGpuPass(CompiledPass pass)
    {
        var key = pass.VertexCso + "|" + pass.PixelCso;
        if (_gpuPasses.TryGetValue(key, out var cached)) return cached;

        try
        {
            var vsBytes = File.ReadAllBytes(pass.VertexCso);
            var psBytes = File.ReadAllBytes(pass.PixelCso);
            var vs = _device.CreateVertexShader(vsBytes);
            var ps = _device.CreatePixelShader(psBytes);
            var layout = CreateInputLayout(pass.Vertex.Inputs, vsBytes);
            if (layout is null) return null;

            var gpu = new GpuPass
            {
                Vs = vs,
                Ps = ps,
                Layout = layout,
                VsGlobalsBind = BindPoint(pass.Vertex, "$Globals"),
                PsGlobalsBind = BindPoint(pass.Pixel, "$Globals"),
                VsGlobals = CreateDynamicBuffer(GlobalsSize(pass.Vertex)),
                PsGlobals = CreateDynamicBuffer(GlobalsSize(pass.Pixel))
            };

            _gpuPasses[key] = gpu;
            return gpu;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private ID3D11InputLayout? CreateInputLayout(IEnumerable<string> inputs, byte[] vsBytes)
    {
        var elements = new List<InputElementDescription>();
        foreach (var input in inputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var semantic = input;
            var index = 0u;
            var digitIndex = semantic.Length;
            while (digitIndex > 0 && char.IsDigit(semantic[digitIndex - 1])) digitIndex--;
            if (digitIndex < semantic.Length)
            {
                uint.TryParse(semantic[digitIndex..], out index);
                semantic = semantic[..digitIndex];
            }

            if (semantic.StartsWith("SV_", StringComparison.OrdinalIgnoreCase)) continue;

            var offset = semantic.ToUpperInvariant() switch
            {
                "POSITION" => HlslVertexLayout.PositionOffset,
                "NORMAL" => HlslVertexLayout.NormalOffset,
                "TANGENT" => HlslVertexLayout.TangentOffset,
                "COLOR" => HlslVertexLayout.ColorOffset,
                "TEXCOORD" when index == 0 => HlslVertexLayout.Uv0Offset,
                "TEXCOORD" when index == 1 => HlslVertexLayout.Uv1Offset,
                "TEXCOORD" when index == 2 => HlslVertexLayout.Uv2Offset,
                "TEXCOORD" => HlslVertexLayout.Uv3Offset,
                _ => HlslVertexLayout.ColorOffset
            };

            elements.Add(new InputElementDescription(semantic, index, Format.R32G32B32A32_Float, offset, 0));
        }

        if (elements.Count == 0) return null;
        return _device.CreateInputLayout(elements.ToArray(), vsBytes);
    }

    private ID3D11Texture2D CreateTexture(byte[] rgba, out ID3D11ShaderResourceView view)
    {
        var texture = _device.CreateTexture2D(rgba, Format.R8G8B8A8_UNorm, 1, 1, 1, 1,
            BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
        view = _device.CreateShaderResourceView(texture, null);
        return texture;
    }

    private ID3D11ShaderResourceView LoadTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out var cached)) return cached;

        ID3D11ShaderResourceView view;
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            using var texture = _device.CreateTexture2D(image.Data, Format.R8G8B8A8_UNorm,
                (uint)image.Width, (uint)image.Height, 1, 1, BindFlags.ShaderResource,
                ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
            view = _device.CreateShaderResourceView(texture, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            view = _whiteView;
        }

        _textureCache[path] = view;
        return view;
    }

    private List<TextureBinding> ResolveTextures(CompiledPass pass, PreviewMaterial? material)
    {
        var result = new List<TextureBinding>();
        Add(pass.Vertex, true);
        Add(pass.Pixel, false);
        return result;

        void Add(ShaderReflection reflection, bool stage)
        {
            foreach (var resource in reflection.Resources)
            {
                if (!string.Equals(resource.Kind, "Texture", StringComparison.OrdinalIgnoreCase)) continue;
                if (resource.Dimension.Length > 0 &&
                    !string.Equals(resource.Dimension, "Texture2D", StringComparison.OrdinalIgnoreCase))
                    continue;

                var view = ResolveTexture(resource.Name, material);
                result.Add(new TextureBinding { Slot = resource.BindPoint, View = view, Stage = stage, Name = resource.Name });
            }
        }
    }

    private ID3D11ShaderResourceView ResolveTexture(string name, PreviewMaterial? material)
    {
        var path = string.Empty;
        if (material is not null)
        {
            if (material.Textures.TryGetValue(name, out var mapped)) path = mapped;
            else if (name.Contains("BaseMap", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("MainTex", StringComparison.OrdinalIgnoreCase))
                path = material.BaseTexture;
            else if (name.Contains("Bump", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Normal", StringComparison.OrdinalIgnoreCase))
                path = material.NormalTexture;
            else if (name.Contains("Emission", StringComparison.OrdinalIgnoreCase))
                path = material.EmissionTexture;
            else if (name.Contains("MatCap", StringComparison.OrdinalIgnoreCase))
                path = material.MatCapTexture;
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return LoadTexture(path);

        if (name.Contains("Bump", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Normal", StringComparison.OrdinalIgnoreCase))
            return _normalView;

        if (name.Contains("MatCap", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Reflection", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Audio", StringComparison.OrdinalIgnoreCase))
            return _blackView;

        return _whiteView;
    }

    private List<SamplerBinding> ResolveSamplers(CompiledPass pass)
    {
        var result = new List<SamplerBinding>();
        Add(pass.Vertex, true);
        Add(pass.Pixel, false);
        return result;

        void Add(ShaderReflection reflection, bool stage)
        {
            foreach (var resource in reflection.Resources)
            {
                if (!string.Equals(resource.Kind, "Sampler", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new SamplerBinding
                {
                    Name = resource.Name,
                    Slot = resource.BindPoint,
                    Stage = stage,
                    Clamp = resource.Name.Contains("clamp", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
    }

    private static uint BindPoint(ShaderReflection reflection, string name)
    {
        var buffer = reflection.Buffers.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return buffer?.BindPoint ?? uint.MaxValue;
    }

    private static uint GlobalsSize(ShaderReflection reflection)
    {
        var buffer = reflection.Buffers.FirstOrDefault(item => string.Equals(item.Name, "$Globals", StringComparison.Ordinal));
        return buffer is null ? 16 : Math.Max(16, (buffer.Size + 15) / 16 * 16);
    }

    private byte[] BuildGlobals(ShaderReflection reflection, CompiledShader shader, PreviewMaterial? material)
    {
        var buffer = reflection.Buffers.FirstOrDefault(item => string.Equals(item.Name, "$Globals", StringComparison.Ordinal));
        if (buffer is null) return new byte[16];

        var data = new byte[Math.Max(16, (buffer.Size + 15) / 16 * 16)];
        foreach (var variable in buffer.Variables)
        {
            var values = Values(variable.Name, shader, material);
            if (values.Length == 0) continue;

            var count = (int)Math.Min(values.Length, variable.Size / 4);
            var integer = IsInteger(variable.Type);
            for (var i = 0; i < count; i++)
            {
                var offset = (int)variable.Offset + i * 4;
                if (offset + 4 > data.Length) break;
                if (integer) BitConverter.TryWriteBytes(data.AsSpan(offset), (int)MathF.Round(values[i]));
                else BitConverter.TryWriteBytes(data.AsSpan(offset), values[i]);
            }
        }

        return data;
    }

    private static bool IsInteger(string type) =>
        type.Contains("Int", StringComparison.OrdinalIgnoreCase) ||
        type.Contains("Bool", StringComparison.OrdinalIgnoreCase);

    private static float[] Values(string name, CompiledShader shader, PreviewMaterial? material)
    {
        if (name.EndsWith("_ST", StringComparison.Ordinal))
        {
            var baseName = name[..^3];
            if (material is not null && material.TextureST.TryGetValue(baseName, out var st)) return st;
            return [1, 1, 0, 0];
        }

        if (material is null)
            return name is "_Color" or "_BaseColor" or "_MainColor" ? [1, 1, 1, 1] : [];

        if (material.Colors.TryGetValue(name, out var color)) return color;
        if (material.Floats.TryGetValue(name, out var number)) return [number];

        if (name is "_Color" or "_BaseColor" or "_MainColor" or "_Tint") return material.BaseColor;
        if (name == "_Cutoff") return [material.Cutoff];
        if (name == "_TransparentMode") return [material.TransparentMode];
        if (name == "_UseOutline") return [material.UseOutline ? 1 : 0];
        if (name is "_UseMatCap" or "_UseMatCap2nd" or "_UseReflection") return [0];
        if (name == "_OutlineWidth") return [material.OutlineWidth];
        if (name == "_BumpScale") return [material.NormalScale];
        if (name == "_EmissionColor") return material.EmissionColor;
        if (name == "_MatCapColor") return material.MatCapColor;

        if (shader.PropertyDefaults.TryGetValue(name, out var defaults)) return defaults;

        return [];
    }

    private static (Vector3 target, float halfHeight) FitView(HlslSceneData scene, ViewSpec spec, float aspect)
    {
        var radius = MathF.Max(scene.Radius, 0.05f);
        var direction = new Vector3(
            MathF.Sin(spec.Yaw) * MathF.Cos(spec.Pitch),
            MathF.Sin(spec.Pitch),
            MathF.Cos(spec.Yaw) * MathF.Cos(spec.Pitch));
        var right = Vector3.Normalize(Vector3.Cross(direction, spec.Up));
        var up = Vector3.Cross(right, direction);

        var target = scene.Center;
        var halfHeight = radius * 1.18f;
        if (scene.BoundsMax.X > scene.BoundsMin.X)
        {
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? scene.BoundsMin.X : scene.BoundsMax.X,
                    (i & 2) == 0 ? scene.BoundsMin.Y : scene.BoundsMax.Y,
                    (i & 4) == 0 ? scene.BoundsMin.Z : scene.BoundsMax.Z) - scene.Center;
                var px = Vector3.Dot(corner, right);
                var py = Vector3.Dot(corner, up);
                minX = MathF.Min(minX, px);
                maxX = MathF.Max(maxX, px);
                minY = MathF.Min(minY, py);
                maxY = MathF.Max(maxY, py);
            }

            var midX = (minX + maxX) * 0.5f;
            var midY = (minY + maxY) * 0.5f;
            target += right * midX + up * midY;
            halfHeight = MathF.Max(MathF.Max(maxY - midY, midY - minY),
                MathF.Max(maxX - midX, midX - minX) / MathF.Max(aspect, 0.001f)) * 1.12f;
        }

        return (target, halfHeight);
    }

    private static (Matrix4x4 view, Matrix4x4 projection, Vector3 eye) CreateCamera(HlslSceneData scene,
        ViewSpec spec, float aspect, float? forcedHalfHeight)
    {
        var radius = MathF.Max(scene.Radius, 0.05f);
        var distance = radius * 4f;
        var direction = new Vector3(
            MathF.Sin(spec.Yaw) * MathF.Cos(spec.Pitch),
            MathF.Sin(spec.Pitch),
            MathF.Cos(spec.Yaw) * MathF.Cos(spec.Pitch));
        var (target, halfHeight) = FitView(scene, spec, aspect);
        if (forcedHalfHeight is > 0) halfHeight = forcedHalfHeight.Value;

        var eye = target + direction * distance;
        var view = LookAt(eye, target, spec.Up);
        var projection = Orthographic(halfHeight, aspect, 0.01f, distance + radius * 4f);
        return (view, projection, eye);
    }

    private static Matrix4x4 LookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        var z = Vector3.Normalize(eye - target);
        var x = Vector3.Normalize(Vector3.Cross(z, up));
        var y = Vector3.Cross(x, z);

        return new Matrix4x4(
            x.X, y.X, z.X, 0,
            x.Y, y.Y, z.Y, 0,
            x.Z, y.Z, z.Z, 0,
            -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1);
    }

    private static Matrix4x4 Orthographic(float halfHeight, float aspect, float near, float far)
    {
        var halfWidth = halfHeight * aspect;
        return new Matrix4x4(
            1f / halfWidth, 0, 0, 0,
            0, 1f / halfHeight, 0, 0,
            0, 0, 1f / (near - far), 0,
            0, 0, near / (near - far), 1);
    }

    private static byte[] BuildEngine(Matrix4x4 world, Matrix4x4 inverse, (Matrix4x4 view, Matrix4x4 projection,
        Vector3 eye) camera, int width, int height, float ticks)
    {
        var data = new byte[880];
        var writer = new SpanWriter(data);

        WriteMatrix(ref writer, Matrix4x4.Transpose(world));
        WriteMatrix(ref writer, inverse);
        WriteMatrix(ref writer, Matrix4x4.Transpose(camera.view));
        WriteMatrix(ref writer, Matrix4x4.Transpose(camera.view * camera.projection));
        WriteMatrix(ref writer, Matrix4x4.Transpose(camera.projection));
        WriteMatrix(ref writer, Matrix4x4.Transpose(world * camera.view * camera.projection));

        var toLight = Vector3.Normalize(new Vector3(0.32f, 0.62f, 0.72f));
        WriteVector(ref writer, new Vector4(camera.eye, 1));
        WriteVector(ref writer, new Vector4(toLight, 0));
        WriteVector(ref writer, new Vector4(1, 1, 1, 1));
        WriteVector(ref writer, new Vector4(toLight, 0));
        WriteVector(ref writer, new Vector4(1, 1, 1, 1));

        WriteVector(ref writer, new Vector4(ticks / 20, ticks, ticks * 2, ticks * 3));
        WriteVector(ref writer, new Vector4(MathF.Sin(ticks / 8), MathF.Sin(ticks / 4), MathF.Sin(ticks / 2), MathF.Sin(ticks)));
        WriteVector(ref writer, new Vector4(MathF.Cos(ticks / 8), MathF.Cos(ticks / 4), MathF.Cos(ticks / 2), MathF.Cos(ticks)));
        WriteVector(ref writer, new Vector4(1f / 60f, 60, 1f / 60f, 60));
        WriteVector(ref writer, new Vector4(1, 0.05f, 200, 1f / 200));
        WriteVector(ref writer, new Vector4(width, height, 1f + 1f / width, 1f + 1f / height));
        WriteVector(ref writer, Vector4.Zero);
        WriteVector(ref writer, new Vector4(0.24f, 0.27f, 0.32f, 1));
        WriteVector(ref writer, new Vector4(0.22f, 0.25f, 0.30f, 1));
        WriteVector(ref writer, new Vector4(0.20f, 0.23f, 0.28f, 1));

        WriteVector(ref writer, Vector4.Zero);
        WriteVector(ref writer, Vector4.Zero);
        WriteVector(ref writer, Vector4.Zero);
        WriteVector(ref writer, new Vector4(0.22f, 0.25f, 0.30f, 1));
        WriteVector(ref writer, new Vector4(0.20f, 0.23f, 0.28f, 1));
        WriteVector(ref writer, new Vector4(0.18f, 0.20f, 0.24f, 1));
        WriteVector(ref writer, Vector4.Zero);
        WriteVector(ref writer, Vector4.Zero);

        WriteMatrix(ref writer, Matrix4x4.Transpose(camera.projection));
        Matrix4x4.Invert(camera.projection, out var inverseProjection);
        WriteMatrix(ref writer, Matrix4x4.Transpose(inverseProjection));

        return data;
    }

    private static void WriteMatrix(ref SpanWriter writer, Matrix4x4 matrix)
    {
        matrix = Matrix4x4.Transpose(matrix);
        writer.Write(matrix.M11); writer.Write(matrix.M12); writer.Write(matrix.M13); writer.Write(matrix.M14);
        writer.Write(matrix.M21); writer.Write(matrix.M22); writer.Write(matrix.M23); writer.Write(matrix.M24);
        writer.Write(matrix.M31); writer.Write(matrix.M32); writer.Write(matrix.M33); writer.Write(matrix.M34);
        writer.Write(matrix.M41); writer.Write(matrix.M42); writer.Write(matrix.M43); writer.Write(matrix.M44);
    }

    private static void WriteVector(ref SpanWriter writer, Vector4 vector)
    {
        writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); writer.Write(vector.W);
    }

    private void EnsureTargets(int width, int height)
    {
        if (_width == width && _height == height && _color is not null) return;

        _colorView?.Dispose();
        _depthView?.Dispose();
        _color?.Dispose();
        _depth?.Dispose();
        _staging?.Dispose();

        _color = _device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, null,
            BindFlags.RenderTarget, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
        _colorView = _device.CreateRenderTargetView(_color, null);
        _depth = _device.CreateTexture2D(Format.D24_UNorm_S8_UInt, (uint)width, (uint)height, 1, 1, null,
            BindFlags.DepthStencil, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
        _depthView = _device.CreateDepthStencilView(_depth, null);
        _staging = _device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, null,
            BindFlags.None, ResourceOptionFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);

        _width = width;
        _height = height;
    }

    private byte[] ReadBack(int width, int height)
    {
        _context.CopyResource(_staging!, _color!);
        var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rgba = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var source = mapped.DataPointer + (nint)(y * (int)mapped.RowPitch);
                Marshal.Copy(source, rgba, y * width * 4, width * 4);
            }

            for (var i = 0; i < rgba.Length; i += 4)
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);

            using var stream = new MemoryStream();
            new ImageWriter().WriteJpg(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream, 90);
            return stream.ToArray();
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
    }

    private ID3D11Buffer CreateDynamicBuffer(uint size)
    {
        return _device.CreateBuffer(new byte[Math.Max(16u, (size + 15) / 16 * 16)],
            new BufferDescription(size, BindFlags.ConstantBuffer, ResourceUsage.Dynamic,
                CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
    }

    private void Upload(ID3D11Buffer buffer, byte[] data)
    {
        var mapped = _context.Map(buffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var count = data.Length;
            Marshal.Copy(data, 0, mapped.DataPointer, count);
        }
        finally
        {
            _context.Unmap(buffer);
        }
    }

    private static ResolvedState DebugState(PreviewMaterial? material, bool wireframe) => new()
    {
        Cull = wireframe ? CullMode.None : material?.DoubleSided == true ? CullMode.None : CullMode.Back,
        ZWrite = true,
        ZTest = ComparisonFunction.LessEqual,
        BlendEnabled = false,
        WriteMask = ColorWriteEnable.All,
        FillMode = wireframe ? FillMode.Wireframe : FillMode.Solid
    };

    private ResolvedState FallbackState(PreviewMaterial? material)
    {
        var blend = material is not null &&
                    string.Equals(material.AlphaMode, "blend", StringComparison.OrdinalIgnoreCase);
        return new ResolvedState
        {
            Cull = material?.DoubleSided == true ? CullMode.None : CullMode.Back,
            ZWrite = !blend,
            ZTest = ComparisonFunction.LessEqual,
            BlendEnabled = blend,
            SrcBlend = Blend.SourceAlpha,
            DstBlend = Blend.InverseSourceAlpha,
            SrcBlendAlpha = Blend.One,
            DstBlendAlpha = Blend.InverseSourceAlpha,
            BlendOp = BlendOperation.Add,
            BlendOpAlpha = BlendOperation.Add,
            WriteMask = ColorWriteEnable.All,
            DepthBias = 0,
            SlopeBias = 0,
            AlphaToCoverage = false
        };
    }

    private static ResolvedState ResolveState(ShaderLabPassInfo pass, PreviewMaterial? material, CompiledShader shader)
    {
        var cull = Resolve(pass.Cull, material, shader, 2);
        var zWrite = Resolve(pass.ZWrite, material, shader, 1);
        var zTest = Resolve(pass.ZTest, material, shader, 4);
        var src = Resolve(pass.SrcBlend, material, shader, 1);
        var dst = Resolve(pass.DstBlend, material, shader, 0);
        var srcAlpha = Resolve(pass.SrcBlendAlpha, material, shader, (int)src);
        var dstAlpha = Resolve(pass.DstBlendAlpha, material, shader, (int)dst);
        var blendOp = Resolve(pass.BlendOp, material, shader, 0);
        var blendOpAlpha = Resolve(pass.BlendOpAlpha, material, shader, (int)blendOp);
        var mask = Resolve(pass.ColorMask, material, shader, 15);
        var offsetFactor = Resolve(pass.OffsetFactor, material, shader, 0);
        var offsetUnits = Resolve(pass.OffsetUnits, material, shader, 0);
        var alphaToMask = Resolve(pass.AlphaToMask, material, shader, 0);

        var blendEnabled = src != 1 || dst != 0;

        return new ResolvedState
        {
            Cull = cull switch { 0 => CullMode.None, 1 => CullMode.Front, _ => CullMode.Back },
            ZWrite = zWrite != 0,
            ZTest = zTest switch
            {
                0 => ComparisonFunction.Always,
                1 => ComparisonFunction.Never,
                2 => ComparisonFunction.Less,
                3 => ComparisonFunction.Equal,
                4 => ComparisonFunction.LessEqual,
                5 => ComparisonFunction.Greater,
                6 => ComparisonFunction.NotEqual,
                7 => ComparisonFunction.GreaterEqual,
                _ => ComparisonFunction.Always
            },
            BlendEnabled = blendEnabled,
            SrcBlend = ToBlend(src),
            DstBlend = ToBlend(dst),
            SrcBlendAlpha = ToAlphaBlend(srcAlpha),
            DstBlendAlpha = ToAlphaBlend(dstAlpha),
            BlendOp = ToBlendOperation(blendOp),
            BlendOpAlpha = ToBlendOperation(blendOpAlpha),
            WriteMask = ToMask(mask),
            DepthBias = offsetUnits,
            SlopeBias = offsetFactor,
            AlphaToCoverage = alphaToMask != 0
        };
    }

    private static int Resolve(string expression, PreviewMaterial? material, CompiledShader shader, int fallback)
    {
        var text = expression.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            var name = text[1..^1].Trim();
            if (material is not null && material.Floats.TryGetValue(name, out var value)) return (int)MathF.Round(value);
            if (shader.PropertyDefaults.TryGetValue(name, out var defaults) && defaults.Length > 0)
                return (int)MathF.Round(defaults[0]);
            return name switch
            {
                "_SrcBlend" => 1,
                "_DstBlend" => 0,
                "_ZWrite" => 1,
                "_ZTest" => 4,
                "_Cull" => 2,
                "_ColorMask" => 15,
                _ => fallback
            };
        }

        if (int.TryParse(text, out var number)) return number;

        return text.ToLowerInvariant() switch
        {
            "off" => 0,
            "on" => 1,
            "back" => 2,
            "front" => 1,
            "one" => 1,
            "zero" => 0,
            "srcalpha" => 5,
            "oneminussrcalpha" => 6,
            "add" => 0,
            "sub" => 1,
            "revsub" => 2,
            "min" => 3,
            "max" => 4,
            _ => fallback
        };
    }

    private static Blend ToBlend(int value) => value switch
    {
        0 => Blend.Zero,
        1 => Blend.One,
        2 => Blend.DestinationColor,
        3 => Blend.SourceColor,
        4 => Blend.InverseDestinationColor,
        5 => Blend.SourceAlpha,
        6 => Blend.InverseSourceAlpha,
        7 => Blend.DestinationAlpha,
        8 => Blend.InverseDestinationAlpha,
        9 => Blend.SourceAlphaSaturate,
        10 => Blend.InverseSourceColor,
        _ => Blend.One
    };

    private static Blend ToAlphaBlend(int value) => value switch
    {
        3 => Blend.SourceAlpha,
        4 => Blend.InverseSourceAlpha,
        9 => Blend.DestinationAlpha,
        10 => Blend.InverseDestinationAlpha,
        11 => Blend.One,
        _ => ToBlend(value)
    };

    private static BlendOperation ToBlendOperation(int value) => value switch
    {
        1 => BlendOperation.Subtract,
        2 => BlendOperation.ReverseSubtract,
        3 => BlendOperation.Min,
        4 => BlendOperation.Max,
        _ => BlendOperation.Add
    };

    private static ColorWriteEnable ToMask(int value)
    {
        var mask = ColorWriteEnable.None;
        if ((value & 1) != 0) mask |= ColorWriteEnable.Red;
        if ((value & 2) != 0) mask |= ColorWriteEnable.Green;
        if ((value & 4) != 0) mask |= ColorWriteEnable.Blue;
        if ((value & 8) != 0) mask |= ColorWriteEnable.Alpha;
        return mask;
    }

    private ID3D11RasterizerState Raster(ResolvedState state)
    {
        var key = $"{state.Cull}|{state.DepthBias}|{state.SlopeBias}|{(int)state.FillMode}";
        if (_rasterCache.TryGetValue(key, out var cached)) return cached;

        var description = new RasterizerDescription(state.Cull, state.FillMode)
        {
            DepthClipEnable = true,
            FrontCounterClockwise = false,
            DepthBias = state.DepthBias,
            SlopeScaledDepthBias = state.SlopeBias,
            MultisampleEnable = false
        };
        var result = _device.CreateRasterizerState(description);
        _rasterCache[key] = result;
        return result;
    }

    private ID3D11DepthStencilState Depth(ResolvedState state)
    {
        var key = $"{state.ZWrite}|{(int)state.ZTest}";
        if (_depthCache.TryGetValue(key, out var cached)) return cached;

        var result = _device.CreateDepthStencilState(new DepthStencilDescription(true,
            state.ZWrite ? DepthWriteMask.All : DepthWriteMask.Zero, state.ZTest));
        _depthCache[key] = result;
        return result;
    }

    private ID3D11BlendState BlendState(ResolvedState state)
    {
        var key =
            $"{(state.BlendEnabled ? 1 : 0)}|{(int)state.SrcBlend}|{(int)state.DstBlend}|{(int)state.SrcBlendAlpha}|{(int)state.DstBlendAlpha}|{(int)state.BlendOp}|{(int)state.BlendOpAlpha}|{(int)state.WriteMask}|{(state.AlphaToCoverage ? 1 : 0)}";
        if (_blendCache.TryGetValue(key, out var cached)) return cached;

        var description = new BlendDescription(Blend.One, Blend.Zero)
        {
            AlphaToCoverageEnable = state.AlphaToCoverage
        };
        ref var target = ref description.RenderTarget[0];
        target.BlendEnable = state.BlendEnabled;
        target.SourceBlend = state.SrcBlend;
        target.DestinationBlend = state.DstBlend;
        target.BlendOperation = state.BlendOp;
        target.SourceBlendAlpha = state.SrcBlendAlpha;
        target.DestinationBlendAlpha = state.DstBlendAlpha;
        target.BlendOperationAlpha = state.BlendOpAlpha;
        target.RenderTargetWriteMask = state.WriteMask;

        var result = _device.CreateBlendState(description);
        _blendCache[key] = result;
        return result;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _colorView?.Dispose();
        _depthView?.Dispose();
        _color?.Dispose();
        _depth?.Dispose();
        _staging?.Dispose();
        _engine.Dispose();
        _linearWrap.Dispose();
        _linearClamp.Dispose();
        _whiteView.Dispose();
        _normalView.Dispose();
        _blackView.Dispose();
        _defaultWhite.Dispose();
        _defaultNormal.Dispose();
        _defaultBlack.Dispose();
        foreach (var view in _textureCache.Values.Distinct()) view.Dispose();
        foreach (var state in _blendCache.Values) state.Dispose();
        foreach (var state in _rasterCache.Values) state.Dispose();
        foreach (var state in _depthCache.Values) state.Dispose();
        foreach (var pass in _gpuPasses.Values) pass.Dispose();

        _context.Dispose();
        _device.Dispose();
    }

    private sealed class GpuSession : IDisposable
    {
        public List<GpuDraw> Items { get; } = [];

        public int Materials { get; set; }

        public int Fallbacks { get; set; }

        public void Dispose()
        {
            foreach (var item in Items)
            {
                item.VertexBuffer.Dispose();
                item.IndexBuffer.Dispose();
            }
        }
    }

    private sealed class GpuDraw
    {
        public HlslDrawItem Source { get; set; } = new();

        public ID3D11Buffer VertexBuffer { get; set; } = null!;

        public ID3D11Buffer IndexBuffer { get; set; } = null!;

        public GpuMaterial Material { get; set; } = new();
    }

    private sealed class GpuMaterial
    {
        public List<PassBinding> Passes { get; } = [];
    }

    private sealed class PassBinding
    {
        public GpuPass Gpu { get; set; } = null!;

        public byte[] VsGlobals { get; set; } = [];

        public byte[] PsGlobals { get; set; } = [];

        public List<TextureBinding> Textures { get; set; } = [];

        public List<SamplerBinding> Samplers { get; set; } = [];

        public ID3D11RasterizerState Raster { get; set; } = null!;

        public ID3D11DepthStencilState Depth { get; set; } = null!;

        public ID3D11BlendState Blend { get; set; } = null!;
    }

    private sealed class GpuPass : IDisposable
    {
        public ID3D11VertexShader? Vs { get; set; }

        public ID3D11PixelShader? Ps { get; set; }

        public ID3D11InputLayout? Layout { get; set; }

        public ID3D11Buffer? VsGlobals { get; set; }

        public ID3D11Buffer? PsGlobals { get; set; }

        public uint VsGlobalsBind { get; set; }

        public uint PsGlobalsBind { get; set; }

        public void Dispose()
        {
            Vs?.Dispose();
            Ps?.Dispose();
            Layout?.Dispose();
            VsGlobals?.Dispose();
            PsGlobals?.Dispose();
        }
    }

    private sealed class TextureBinding
    {
        public string Name { get; set; } = string.Empty;

        public uint Slot { get; set; }

        public ID3D11ShaderResourceView View { get; set; } = null!;

        public bool Stage { get; set; }
    }

    private sealed class SamplerBinding
    {
        public string Name { get; set; } = string.Empty;

        public uint Slot { get; set; }

        public bool Stage { get; set; }

        public bool Clamp { get; set; }
    }

    private sealed class ResolvedState
    {
        public CullMode Cull { get; set; }

        public bool ZWrite { get; set; }

        public ComparisonFunction ZTest { get; set; }

        public bool BlendEnabled { get; set; }

        public Blend SrcBlend { get; set; }

        public Blend DstBlend { get; set; }

        public Blend SrcBlendAlpha { get; set; }

        public Blend DstBlendAlpha { get; set; }

        public BlendOperation BlendOp { get; set; }

        public BlendOperation BlendOpAlpha { get; set; }

        public ColorWriteEnable WriteMask { get; set; }

        public int DepthBias { get; set; }

        public float SlopeBias { get; set; }

        public bool AlphaToCoverage { get; set; }

        public FillMode FillMode { get; set; } = FillMode.Solid;
    }

    private sealed record ViewSpec(string Name, float Yaw, float Pitch, Vector3 Up);

    private static class Views
    {
        public static readonly ViewSpec Front = new("front", 0, 0, Vector3.UnitY);
        public static readonly ViewSpec Left = new("left", -MathF.PI / 2, 0, Vector3.UnitY);
        public static readonly ViewSpec Top = new("top", 0, MathF.PI / 2, new Vector3(0, 0, -1));
        public static readonly ViewSpec Iso = new("iso", MathF.PI / 4, 0.6154797f, Vector3.UnitY);

        public static ViewSpec Find(string name) => name.ToLowerInvariant() switch
        {
            "front" => Front,
            "left" => Left,
            "top" => Top,
            "iso" => Iso,
            _ => Front
        };
    }

    private ref struct SpanWriter
    {
        private readonly byte[] _buffer;
        private int _offset;

        public SpanWriter(byte[] buffer)
        {
            _buffer = buffer;
            _offset = 0;
        }

        public void Write(float value)
        {
            BitConverter.TryWriteBytes(_buffer.AsSpan(_offset), value);
            _offset += 4;
        }
    }
}
