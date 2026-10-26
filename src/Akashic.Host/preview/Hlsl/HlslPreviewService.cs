using System.Collections.Concurrent;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview;

namespace Akashic.Host.Preview.Hlsl;

public sealed class HlslPreviewService : IDisposable
{
    private readonly IPreviewService _preview;
    private readonly ShaderCompileService _compiler;
    private readonly ConcurrentDictionary<string, Lazy<Task<HlslSceneData>>> _scenes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PreviewBuildArtifacts> _artifacts = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly D3D11PreviewRenderer? _renderer;
    private readonly string? _rendererError;

    public HlslPreviewService(IPreviewService preview, ILoggerFactory loggerFactory)
    {
        _preview = preview;
        var cacheRoot = PreviewCachePaths.Root;
        _compiler = new ShaderCompileService(cacheRoot, loggerFactory.CreateLogger("hlsl"));

        try
        {
            _renderer = D3D11PreviewRenderer.Create();
        }
        catch (Exception ex)
        {
            _rendererError = ex.Message;
        }
    }

    public (bool Available, string Error, string Adapter) Status()
    {
        if (_renderer is null) return (false, _rendererError ?? "D3D11 不可用。", string.Empty);
        var tool = _compiler.ToolError;
        return (tool is null, tool ?? string.Empty, _renderer.Adapter);
    }

    public async Task<RenderFrame> RenderAsync(RenderOptions options, CancellationToken ct)
    {
        Guard();

        var artifacts = _artifacts.GetOrAdd(options.SessionId, id =>
            _preview.GetArtifacts(id) ?? throw new InvalidOperationException("预览构建不存在，请重新打开预览。"));

        var scene = await SceneAsync(artifacts, options.Gizmo, ct);
        if (scene.Items.Count == 0)
            throw new InvalidOperationException("这个内容里没有可渲染的网格——只有粒子系统、UI 或纯脚本组件时无法预览。");

        return await RenderSceneAsync(scene, options, ct);
    }

    public async Task<RenderFrame> RenderMaterialAsync(PreviewMaterial material, RenderOptions options,
        CancellationToken ct)
    {
        Guard();

        var scene = HlslSceneBuilder.BuildMaterialPreview(material.Path, material);
        return await RenderSceneAsync(scene, options, ct);
    }

    private void Guard()
    {
        if (_renderer is null) throw new InvalidOperationException(_rendererError ?? "D3D11 不可用。");
        if (_compiler.ToolError is { } toolError) throw new InvalidOperationException(toolError);
    }

    private async Task<RenderFrame> RenderSceneAsync(HlslSceneData scene, RenderOptions options, CancellationToken ct)
    {
        var shaders = new Dictionary<PreviewMaterial, CompiledShader>();
        var mode = options.Mode.ToLowerInvariant();
        var debugShader = mode is "clay" or "wireframe"
            ? await _compiler.GetDebugAsync(mode, ct, options.LightBackground)
            : null;
        var gizmoShader = options.Gizmo ? await _compiler.GetDebugAsync("gizmo", ct) : null;
        var fallbackOpaque = await _compiler.GetFallbackAsync(false, ct);
        var fallbackCutout = await _compiler.GetFallbackAsync(true, ct);

        foreach (var material in scene.Materials)
        {
            if (gizmoShader is not null && material.ShaderName == "Akashic/Gizmo")
            {
                shaders[material] = gizmoShader;
                continue;
            }

            if (debugShader is not null)
            {
                shaders[material] = debugShader;
                continue;
            }

            CompiledShader? shader = null;
            if (File.Exists(material.Shader))
            {
                shader = await _compiler.GetAsync(material.Shader, scene.Root, material.Keywords,
                    options.Outline && (material.UseOutline || material.OutlineWidth > 0.0001f), ct);
            }

            if (shader is null || !shader.Ok)
                shader = string.Equals(material.AlphaMode, "mask", StringComparison.OrdinalIgnoreCase)
                    ? fallbackCutout
                    : fallbackOpaque;

            shaders[material] = shader;
        }

        await _renderGate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => _renderer!.Render(scene, shaders, fallbackOpaque, options), ct);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private Task<HlslSceneData> SceneAsync(PreviewBuildArtifacts artifacts, bool gizmo, CancellationToken ct)
    {
        var key = artifacts.SessionId + (gizmo ? "|gizmo" : string.Empty);
        var lazy = _scenes.GetOrAdd(key, _ => new Lazy<Task<HlslSceneData>>(() =>
            Task.Run(() => HlslSceneBuilder.Build(artifacts.SessionId, artifacts.Scene, gizmo), ct)));
        return lazy.Value;
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _renderGate.Dispose();
    }
}
