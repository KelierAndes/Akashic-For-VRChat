using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Akashic.Core.Services.Preview.Shader;
using Microsoft.Extensions.Logging;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11.Shader;
using Akashic.Core.Services.Preview;

namespace Akashic.Host.Preview.Hlsl;

public sealed class ShaderCompileService
{
    private const string CacheVersion = "hlsl-v4";

    private const int PassTimeoutSeconds = 60;

    private const int ShaderBudgetSeconds = 180;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly string _shimRoot;
    private readonly FxcTool? _fxc;
    private readonly KeyedSemaphore _gate = new(KeyedSemaphore.DefaultLimit);
    private readonly ConcurrentDictionary<string, CompiledShader> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _fallbackSource;
    private readonly ILogger? _log;

    public ShaderCompileService(string cacheRoot, ILogger? log = null)
    {
        _log = log;
        _root = PreviewCachePaths.VersionedUnder(cacheRoot, "hlsl", CacheVersion);
        _shimRoot = ShaderEnvironment.Ensure(cacheRoot);
        _fxc = FxcTool.Find();
        _fallbackSource = Path.Combine(AppContext.BaseDirectory, "preview", "hlsl", "FallbackLit.hlsl");
    }

    public string? ToolError => _fxc is null ? "找不到随包的 fxc 编译器。" : null;

    public int MemoryCount => _memory.Count;

    public const int MaxManualKeywords = 2;

    public static IReadOnlyList<string> DeclaredKeywords(string shaderPath)
    {
        try
        {
            return ShaderLabExtractor.Parse(shaderPath).Passes
                .SelectMany(pass => pass.DefaultKeywords)
                .Where(ShaderKeywordRules.IsValid)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(keyword => keyword, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public string BuiltInStandardPath => Path.Combine(_shimRoot, "Standard.shader");

    public string BuiltInUiPath => Path.Combine(_shimRoot, "UIDefault.shader");

    public string? ResolveBuiltIn(string shaderName)
    {
        if (shaderName.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(BuiltInStandardPath))
            return BuiltInStandardPath;

        var ui = shaderName.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) ||
                 shaderName.StartsWith("Sprites/", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(shaderName, "Unlit/Texture", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(shaderName, "Unlit/Color", StringComparison.OrdinalIgnoreCase);

        return ui && File.Exists(BuiltInUiPath) ? BuiltInUiPath : null;
    }

    public async Task<CompiledShader> GetAsync(string shaderPath, string contentRoot,
        IReadOnlyList<string> keywords, bool includeOutline, IReadOnlyList<string> extraKeywords,
        string colorSpace, CancellationToken ct)
    {
        if (_fxc is null)
            return new CompiledShader
            {
                ShaderPath = shaderPath,
                Error = ToolError ?? "HLSL 编译器不可用。",
                Fallback = true
            };

        if (string.IsNullOrWhiteSpace(shaderPath) || !File.Exists(shaderPath))
            return await GetFallbackAsync(false, ct);

        var info = new FileInfo(shaderPath);
        var extras = extraKeywords
            .Where(ShaderKeywordRules.IsValid)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var keywordKey = ShaderKeywordRules.Key(keywords.Concat(extras));
        var key = ShaderCacheKey.For(ShaderEnvironment.Version, shaderPath, info.LastWriteTimeUtc.Ticks,
            keywordKey, includeOutline, colorSpace);

        var metaPath = Path.Combine(_root, key, "meta.json");
        if (_memory.TryGetValue(key, out var memory)) return memory;

        var lease = await _gate.AcquireAsync(KeyedSemaphore.KeyOf(shaderPath), ct);
        try
        {
            if (_memory.TryGetValue(key, out memory)) return memory;

            if (File.Exists(metaPath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CompiledShader>(await File.ReadAllTextAsync(metaPath, ct),
                        JsonOptions);
                    if (cached is not null && cached.Passes.All(pass =>
                            File.Exists(pass.VertexCso) && File.Exists(pass.PixelCso)))
                    {
                        _memory[key] = cached;
                        return cached;
                    }
                }
                catch (JsonException)
                {
                }
            }

            var compiled = await CompileAsync(shaderPath, contentRoot, keywords, includeOutline,
                extras, colorSpace, Path.Combine(_root, key), ct);
            if (!compiled.Ok)
            {
                _log?.LogWarning("HLSL 编译失败 {shader}：{error}", shaderPath, compiled.Error);
                var fallback = await CompileFallbackCoreAsync(key + "-fb", false, ct);
                fallback.Error = compiled.Error;
                fallback.FailureKind = compiled.FailureKind.Length > 0 ? compiled.FailureKind : "compile";
                await WriteMetaAsync(Path.Combine(_root, key + "-fb"), fallback, ct);
                _memory[key] = fallback;
                return fallback;
            }

            await WriteMetaAsync(Path.Combine(_root, key), compiled, ct);
            _memory[key] = compiled;
            return compiled;
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static async Task WriteMetaAsync(string directory, CompiledShader shader, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "meta.json"),
                JsonSerializer.Serialize(shader, JsonOptions), ct);
        }
        catch (IOException)
        {
        }
    }

    public async Task<CompiledShader> GetDebugAsync(string mode, CancellationToken ct, bool light = false)
    {
        var key = mode switch
        {
            "wireframe" => light ? "debug-wireframe-light" : "debug-wireframe",
            "gizmo" => "debug-gizmo",
            _ => light ? "debug-clay-light" : "debug-clay"
        };
        if (_memory.TryGetValue(key, out var memory)) return memory;

        var lease = await _gate.AcquireAsync("builtin:" + key, ct);
        try
        {
            if (_memory.TryGetValue(key, out memory)) return memory;

            var dir = Path.Combine(_root, key);
            var metaPath = Path.Combine(dir, "meta.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CompiledShader>(await File.ReadAllTextAsync(metaPath, ct),
                        JsonOptions);
                    if (cached is not null && cached.Passes.Count > 0)
                    {
                        _memory[key] = cached;
                        return cached;
                    }
                }
                catch (JsonException)
                {
                }
            }

            var result = new CompiledShader
            {
                ShaderPath = _fallbackSource,
                ShaderName = "Akashic/Debug",
                Error = _fxc is null ? ToolError ?? "HLSL 编译器不可用。" : string.Empty
            };

            if (_fxc is null || !File.Exists(_fallbackSource))
            {
                _memory[key] = result;
                return result;
            }

            Directory.CreateDirectory(dir);
            var defines = mode switch
            {
                "wireframe" => new List<string> { "AKASHIC_WIREFRAME" },
                "gizmo" => new List<string> { "AKASHIC_GIZMO" },
                _ => new List<string> { "AKASHIC_CLAY" }
            };
            if (light && mode is "clay" or "wireframe") defines.Add("AKASHIC_LIGHT");
            var info = new ShaderLabPassInfo
            {
                Name = "Debug",
                LightMode = "ForwardBase",
                Cull = mode == "gizmo" ? "Off" : "Back",
                ZWrite = mode == "gizmo" ? "Off" : "On",
                ZTest = mode == "gizmo" ? "Always" : "LEqual"
            };
            var (pass, log, _) = await CompilePassAsync(dir, 0, "Debug", false, info,
                await File.ReadAllTextAsync(_fallbackSource, ct), "vert", "frag", defines,
                [_shimRoot, Path.Combine(_shimRoot, "UnityStubs")],
                TimeSpan.FromSeconds(PassTimeoutSeconds), ct);

            if (pass is null)
            {
                result.Error = "调试着色器编译失败：" + Shorten(log);
                _memory[key] = result;
                return result;
            }

            result.Passes.Add(pass);
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(result, JsonOptions), ct);
            _memory[key] = result;
            return result;
        }
        finally
        {
            lease.Dispose();
        }
    }

    public async Task<CompiledShader> GetFallbackAsync(bool alphaTest, CancellationToken ct, bool unlit = false)
    {
        var key = unlit ? "fallback-unlit" : "fallback-" + (alphaTest ? "cutout" : "opaque");
        if (_memory.TryGetValue(key, out var memory)) return memory;

        var lease = await _gate.AcquireAsync("builtin:" + key, ct);
        try
        {
            if (_memory.TryGetValue(key, out memory)) return memory;

            var dir = Path.Combine(_root, key);
            var metaPath = Path.Combine(dir, "meta.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<CompiledShader>(await File.ReadAllTextAsync(metaPath, ct),
                        JsonOptions);
                    if (cached is not null && cached.Passes.Count > 0)
                    {
                        _memory[key] = cached;
                        return cached;
                    }
                }
                catch (JsonException)
                {
                }
            }

            var compiled = await CompileFallbackCoreAsync(key, alphaTest, ct, unlit);
            if (compiled.Ok)
                await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(compiled, JsonOptions), ct);
            _memory[key] = compiled;
            return compiled;
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task<CompiledShader> CompileFallbackCoreAsync(string key, bool alphaTest, CancellationToken ct,
        bool unlit = false)
    {
        var result = new CompiledShader
        {
            ShaderPath = _fallbackSource,
            ShaderName = unlit ? "Akashic/FallbackUnlit" : "Akashic/FallbackLit",
            Fallback = true
        };

        if (_fxc is null || !File.Exists(_fallbackSource))
        {
            result.Error = ToolError ?? "找不到回退着色器。";
            return result;
        }

        var dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        var defines = new List<string>();
        if (alphaTest) defines.Add("_ALPHATEST_ON");
        if (unlit) defines.Add("AKASHIC_UNLIT");
        var (pass, log, _) = await CompilePassAsync(dir, 0, "Fallback", false,
            new ShaderLabPassInfo { Name = "Fallback", LightMode = "ForwardBase" },
            File.ReadAllText(_fallbackSource), "vert", "frag", defines,
            [_shimRoot, Path.Combine(_shimRoot, "UnityStubs")],
            TimeSpan.FromSeconds(PassTimeoutSeconds), ct);

        if (pass is null)
        {
            result.Error = "回退着色器编译失败：" + Shorten(log);
            return result;
        }

        result.Passes.Add(pass);
        return result;
    }

    private async Task<CompiledShader> CompileAsync(string shaderPath, string contentRoot,
        IReadOnlyList<string> keywords, bool includeOutline, IReadOnlyList<string> extraKeywords,
        string colorSpace, string directory, CancellationToken ct)
    {
        var program = ShaderLabExtractor.Parse(shaderPath);
        var result = new CompiledShader
        {
            ShaderPath = shaderPath,
            ShaderName = program.Name,
            Pipeline = program.Pipeline
        };

        foreach (var property in program.Properties)
        {
            if (property.Numbers.Length > 0)
                result.PropertyDefaults[property.Name] = property.Numbers;
        }

        if (!program.SupportsHlsl)
        {
            result.Error = program.UnsupportedReason.Length > 0
                ? program.UnsupportedReason
                : "着色器不支持 HLSL 编译。";
            result.FailureKind = program.FailureKind.Length > 0 ? program.FailureKind : "unsupported";
            return result;
        }

        var passes = program.Passes
            .Where(pass => pass.LightMode.Length == 0 ||
                           string.Equals(pass.LightMode, "ForwardBase", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(pass.LightMode, "Always", StringComparison.OrdinalIgnoreCase))
            .Where(pass => includeOutline || !pass.IsOutline)
            .ToList();

        if (passes.Count == 0)
        {
            result.Error = "没有前向渲染 Pass。";
            result.FailureKind = "unsupported";
            return result;
        }

        Directory.CreateDirectory(directory);
        var shaderDir = Path.GetDirectoryName(shaderPath) ?? contentRoot;

        var allowedRoots = new List<string>
        {
            shaderDir,
            contentRoot,
            _shimRoot,
            Path.Combine(_shimRoot, "UnityStubs")
        };

        var fxcIncludes = new List<string>
        {
            _shimRoot,
            Path.Combine(_shimRoot, "UnityStubs")
        };

        var engineDefines = keywords.Where(ShaderKeywordRules.IsValid).Distinct(StringComparer.Ordinal).ToList();
        engineDefines.Add("DIRECTIONAL");
        if (!string.Equals(colorSpace, "linear", StringComparison.OrdinalIgnoreCase))
            engineDefines.Add("UNITY_COLORSPACE_GAMMA");

        var budget = Stopwatch.StartNew();

        foreach (var pass in passes)
        {
            var remaining = ShaderBudgetSeconds - budget.Elapsed.TotalSeconds;
            if (remaining <= 0.5)
            {
                result.Error = $"Pass {pass.Name} 尚未开始编译，已超出同一 shader 的 {ShaderBudgetSeconds} 秒预算。";
                result.FailureKind = "timeout";
                break;
            }

            var expanded = ShaderIncludeResolver.Expand(pass.Source, allowedRoots);
            if (expanded.Rejected.Count > 0)
            {
                result.Error = $"Pass {pass.Name} 的 #include 指向允许目录之外：{expanded.Rejected[0]}";
                result.FailureKind = "include";
                continue;
            }

            if (expanded.TooLarge)
            {
                result.Error = $"Pass {pass.Name} 展开 #include 后源码过大。";
                result.FailureKind = "size";
                continue;
            }

            var timeout = TimeSpan.FromSeconds(Math.Min(PassTimeoutSeconds, remaining));
            var defines = new List<string>(engineDefines);
            foreach (var keyword in extraKeywords)
            {
                if (pass.DefaultKeywords.Contains(keyword, StringComparer.Ordinal)) defines.Add(keyword);
            }

            var (compiled, log, timedOut) = await CompilePassAsync(directory, pass.Index, pass.Name, pass.IsOutline,
                ToInfo(pass), expanded.Source, pass.VertexEntry, pass.FragmentEntry, defines, fxcIncludes, timeout, ct);

            if (compiled is null)
            {
                if (timedOut)
                {
                    result.Error = $"Pass {pass.Name} 编译超时（超过 {PassTimeoutSeconds} 秒）。";
                    result.FailureKind = "timeout";
                }
                else
                {
                    result.Error = string.IsNullOrEmpty(result.Error)
                        ? $"Pass {pass.Name} 编译失败：{Shorten(log)}"
                        : result.Error;
                    if (result.FailureKind.Length == 0) result.FailureKind = "compile";
                }

                continue;
            }

            result.Passes.Add(compiled);
        }

        if (result.Passes.Count == 0)
            result.Error = string.IsNullOrEmpty(result.Error) ? "所有 Pass 编译失败。" : result.Error;

        return result;
    }

    private async Task<(CompiledPass? Pass, string Log, bool TimedOut)> CompilePassAsync(string directory, int index,
        string name, bool outline, ShaderLabPassInfo info, string source, string vertexEntry, string fragmentEntry,
        IReadOnlyList<string> defines, IReadOnlyList<string> includeRoots, TimeSpan timeout, CancellationToken ct)
    {
        var baseName = $"pass{index}";
        var sourcePath = Path.Combine(directory, baseName + ".hlsl");
        await File.WriteAllTextAsync(sourcePath, source, ct);

        var vertexCso = Path.Combine(directory, baseName + "-vs.cso");
        var pixelCso = Path.Combine(directory, baseName + "-ps.cso");

        var vs = await _fxc!.CompileAsync(sourcePath, vertexEntry, "vs_5_0", vertexCso, includeRoots, defines,
            timeout, ct);
        if (!vs.Ok) return (null, vs.Log, vs.TimedOut);

        var ps = await _fxc.CompileAsync(sourcePath, fragmentEntry, "ps_5_0", pixelCso, includeRoots, defines,
            timeout, ct);
        if (!ps.Ok) return (null, ps.Log, ps.TimedOut);

        var vertexReflection = ShaderReflectionReader.Read(vertexCso);
        var pixelReflection = ShaderReflectionReader.Read(pixelCso);
        if (vertexReflection is null || pixelReflection is null) return (null, "着色器反射失败。", false);

        return (new CompiledPass
        {
            Index = index,
            Name = name,
            Outline = outline,
            Pass = info,
            VertexCso = vertexCso,
            PixelCso = pixelCso,
            Vertex = vertexReflection,
            Pixel = pixelReflection
        }, string.Empty, false);
    }

    private static string Shorten(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var line = lines.FirstOrDefault(item => item.Contains("error", StringComparison.OrdinalIgnoreCase))
                   ?? lines.FirstOrDefault()
                   ?? string.Empty;
        return line.Trim().Length > 300 ? line.Trim()[..300] : line.Trim();
    }

    private static ShaderLabPassInfo ToInfo(ShaderLabPass pass) => new()
    {
        Name = pass.Name,
        LightMode = pass.LightMode,
        IsOutline = pass.IsOutline,
        Cull = pass.Cull,
        ZWrite = pass.ZWrite,
        ZTest = pass.ZTest,
        SrcBlend = pass.SrcBlend,
        DstBlend = pass.DstBlend,
        SrcBlendAlpha = pass.SrcBlendAlpha,
        DstBlendAlpha = pass.DstBlendAlpha,
        BlendOp = pass.BlendOp,
        BlendOpAlpha = pass.BlendOpAlpha,
        ColorMask = pass.ColorMask,
        OffsetFactor = pass.OffsetFactor,
        OffsetUnits = pass.OffsetUnits,
        AlphaToMask = pass.AlphaToMask
    };
}

public static class ShaderReflectionReader
{
    public static ShaderReflection? Read(string csoPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(csoPath);
            using var reflection = Compiler.Reflect<ID3D11ShaderReflection>(bytes);
            var result = new ShaderReflection();
            var description = reflection.Description;

            var bindPoints = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0u; i < description.BoundResources; i++)
            {
                var resource = reflection.GetResourceBindingDescription(i);
                result.Resources.Add(new ShaderResource
                {
                    Name = resource.Name,
                    Kind = resource.Type.ToString(),
                    Dimension = resource.Dimension.ToString(),
                    BindPoint = resource.BindPoint
                });

                if (resource.Type == ShaderInputType.ConstantBuffer && !bindPoints.ContainsKey(resource.Name))
                    bindPoints[resource.Name] = resource.BindPoint;
            }

            for (var i = 0u; i < description.ConstantBuffers; i++)
            {
                var buffer = reflection.GetConstantBufferByIndex(i);
                var info = buffer.Description;
                var shaderBuffer = new ShaderBuffer
                {
                    Name = info.Name,
                    Size = info.Size,
                    BindPoint = bindPoints.TryGetValue(info.Name, out var bind) ? bind : 0
                };

                for (var j = 0u; j < info.VariableCount; j++)
                {
                    var variable = buffer.GetVariableByIndex(j);
                    shaderBuffer.Variables.Add(new ShaderUniform
                    {
                        Name = variable.Description.Name,
                        Offset = variable.Description.StartOffset,
                        Size = variable.Description.Size,
                        Type = variable.VariableType.Description.Type.ToString()
                    });
                }

                result.Buffers.Add(shaderBuffer);
            }

            for (var i = 0u; i < description.InputParameters; i++)
            {
                var parameter = reflection.GetInputParameterDescription(i);
                result.Inputs.Add($"{parameter.SemanticName}{parameter.SemanticIndex}");
            }

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
