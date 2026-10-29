using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services.Preview.Shader;

public sealed class ShaderLabProperty
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Default { get; set; } = string.Empty;

    public float[] Numbers { get; set; } = [];
}

public sealed class ShaderLabPass
{
    public string Name { get; set; } = string.Empty;

    public int Index { get; set; }

    public string LightMode { get; set; } = string.Empty;

    public bool IsOutline { get; set; }

    public string VertexEntry { get; set; } = "vert";

    public string FragmentEntry { get; set; } = "frag";

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

    public List<string> DefaultKeywords { get; set; } = [];

    public string Source { get; set; } = string.Empty;
}

public sealed class ShaderLabProgram
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Pipeline { get; set; } = "builtin";

    public bool SupportsHlsl { get; set; }

    public string UnsupportedReason { get; set; } = string.Empty;

    public string FailureKind { get; set; } = string.Empty;

    public List<ShaderLabProperty> Properties { get; set; } = [];

    public List<ShaderLabPass> Passes { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public static class ShaderLabExtractor
{
    public const int MaxShaderBytes = 2 * 1024 * 1024;

    private const int MaxPassChars = 4 * 1024 * 1024;

    private static readonly Regex ShaderRegex = new(
        @"\bShader\s+""(?<name>[^""]+)""\s*\{", RegexOptions.Compiled);

    private static readonly Regex PropertyRegex = new(
        @"^\s*(?<flags>(?:\[[^\]]*\]\s*)*)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(\s*""[^""]*""\s*,\s*(?<type>(?:[^()]|\([^()]*\))+)\)\s*=\s*(?<default>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TagRegex = new(
        @"""([^""]+)""\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex StateRegex = new(
        @"^[ \t]*(?<key>Cull|ZWrite|ZTest|ZClip|ColorMask|AlphaToMask|BlendOp|Blend|Offset)[ \t]+(?<value>[^\r\n]+?)[ \t\r]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, (DateTime Stamp, ShaderLabProgram Program)> Parsed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string?> ShaderNames = new(StringComparer.OrdinalIgnoreCase);

    public static ShaderLabProgram Parse(string path, int depth = 0)
    {
        if (depth > 4) return new ShaderLabProgram { Path = path, UnsupportedReason = "UsePass 嵌套过深。" };

        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        lock (Parsed)
        {
            if (Parsed.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Program;
        }

        var program = ParseCore(path, depth);
        lock (Parsed) Parsed[path] = (stamp, program);
        return program;
    }

    private static ShaderLabProgram ParseCore(string path, int depth)
    {
        var program = new ShaderLabProgram { Path = path };
        string text;

        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxShaderBytes)
            {
                program.UnsupportedReason = "着色器源码过大（超过 2 MB）。";
                program.FailureKind = "size";
                return program;
            }

            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            program.UnsupportedReason = "着色器文件读取失败。";
            return program;
        }
        catch (UnauthorizedAccessException)
        {
            program.UnsupportedReason = "着色器文件读取失败。";
            return program;
        }

        var clean = Clean(text);

        var shaderMatch = ShaderRegex.Match(text);
        if (!shaderMatch.Success)
        {
            program.UnsupportedReason = Path.GetExtension(path).Equals(".shadergraph", StringComparison.OrdinalIgnoreCase)
                ? "Shader Graph 生成的着色器，没有可解析的 ShaderLab 源码。"
                : "不是 ShaderLab 文件。";
            return program;
        }

        program.Name = shaderMatch.Groups["name"].Value.Trim();
        var shaderOpen = clean.IndexOf('{', shaderMatch.Index);
        var shaderClose = MatchBrace(clean, shaderOpen);
        if (shaderOpen < 0 || shaderClose < 0) return program;

        var shaderBody = text[(shaderOpen + 1)..shaderClose];
        var cleanBody = clean[(shaderOpen + 1)..shaderClose];

        program.Pipeline = text.Contains("com.unity.render-pipelines", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("Universal", StringComparison.Ordinal)
            ? "urp"
            : "builtin";

        var properties = FindTopLevelBlock(text, cleanBody, shaderOpen + 1, "Properties");
        if (properties is not null)
            program.Properties = ParseProperties(text[properties.Value.Item1..properties.Value.Item2]);

        var topIncludes = new StringBuilder();
        foreach (var block in FindTopLevelBlocks(text, cleanBody, shaderOpen + 1, "CGINCLUDE", "HLSLINCLUDE"))
            topIncludes.AppendLine(StripPragmas(text[block.Item1..block.Item2]));

        var subShaders = FindTopLevelBlocks(text, cleanBody, shaderOpen + 1, "SubShader");
        if (subShaders.Count == 0)
        {
            program.UnsupportedReason = "没有 SubShader。";
            return program;
        }

        var passIndex = 0;
        foreach (var (subStart, subEnd) in subShaders)
        {
            var openRel = clean.IndexOf('{', subStart);
            if (openRel < 0 || openRel >= subEnd) continue;
            var innerStart = openRel + 1;
            var innerEnd = subEnd - 1;
            if (innerEnd <= innerStart) continue;

            var subClean = clean[innerStart..innerEnd];

            var subIncludes = new StringBuilder();
            foreach (var block in FindTopLevelBlocks(text, subClean, innerStart, "CGINCLUDE", "HLSLINCLUDE"))
                subIncludes.AppendLine(StripPragmas(text[block.Item1..block.Item2]));

            var passes = FindTopLevelBlocks(text, subClean, innerStart, "Pass");
            var parsed = new List<ShaderLabPass>();
            foreach (var (passStart, passEnd) in passes)
            {
                var pass = ParsePass(text, clean, passStart, passEnd, passIndex, topIncludes.ToString(),
                    subIncludes.ToString(), program);
                if (pass is null) continue;

                pass.Index = passIndex++;
                parsed.Add(pass);
            }

            var subText = text[innerStart..innerEnd];
            foreach (Match use in Regex.Matches(subText, @"\bUsePass\s+""(?<reference>[^""]+)"""))
            {
                var reference = use.Groups["reference"].Value.Trim();
                var pass = ResolveUsePass(reference, Path.GetDirectoryName(path) ?? string.Empty, depth);
                if (pass is null)
                {
                    program.Warnings.Add("UsePass 未解析：" + reference);
                    continue;
                }

                pass.Index = passIndex++;
                parsed.Add(pass);
            }

            if (parsed.Count == 0) continue;

            program.Passes = parsed;
            program.SupportsHlsl = program.Pipeline == "builtin" &&
                                   parsed.Any(pass => pass.LightMode is "ForwardBase" or "Always" or "");
            if (!program.SupportsHlsl)
            {
                program.UnsupportedReason = program.Warnings.Count > 0
                    ? program.Warnings[0]
                    : program.Pipeline == "urp"
                        ? "URP 着色器暂不支持 HLSL 精确渲染。"
                        : "没有可用的前向渲染 Pass。";
            }

            return program;
        }

        program.UnsupportedReason = program.Warnings.Count > 0
            ? program.Warnings[0]
            : text.Contains("#pragma surface", StringComparison.OrdinalIgnoreCase)
                ? "Surface Shader 的 Pass 由 Unity 生成，没有可复用的源码。"
                : "没有可渲染的 Pass。";
        return program;
    }

    private static ShaderLabPass? ResolveUsePass(string reference, string shaderDirectory, int depth)
    {
        var slash = reference.LastIndexOf('/');
        if (slash <= 0 || slash >= reference.Length - 1) return null;

        var shaderName = reference[..slash];
        var passName = reference[(slash + 1)..];
        var shaderPath = FindShaderByName(shaderName, shaderDirectory);
        if (shaderPath is null) return null;

        var referenced = Parse(shaderPath, depth + 1);
        var pass = referenced.Passes.FirstOrDefault(item =>
            string.Equals(item.Name, passName, StringComparison.OrdinalIgnoreCase));
        if (pass is null) return null;

        return new ShaderLabPass
        {
            Name = pass.Name,
            LightMode = pass.LightMode,
            IsOutline = pass.IsOutline,
            VertexEntry = pass.VertexEntry,
            FragmentEntry = pass.FragmentEntry,
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
            AlphaToMask = pass.AlphaToMask,
            DefaultKeywords = [.. pass.DefaultKeywords],
            Source = pass.Source
        };
    }

    private static string? FindShaderByName(string shaderName, string directory)
    {
        var key = directory + "|" + shaderName;
        lock (ShaderNames)
        {
            if (ShaderNames.TryGetValue(key, out var known)) return known;
        }

        string? found = null;
        var roots = new List<string> { directory };
        var parent = Path.GetDirectoryName(directory);
        if (!string.IsNullOrEmpty(parent) && !string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
            roots.Add(parent);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.shader", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var reader = new StreamReader(file);
                    var buffer = new char[4096];
                    var read = reader.Read(buffer, 0, buffer.Length);
                    var head = new string(buffer, 0, read);
                    var match = ShaderRegex.Match(head);
                    if (!match.Success) continue;
                    if (!string.Equals(match.Groups["name"].Value.Trim(), shaderName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found = file;
                    break;
                }
                catch (IOException)
                {
                }
            }

            if (found is not null) break;
        }

        lock (ShaderNames) ShaderNames[key] = found;
        return found;
    }

    private static ShaderLabPass? ParsePass(string text, string clean, int start, int end, int index,
        string topIncludes, string subIncludes, ShaderLabProgram program)
    {
        var body = text[start..end];
        var nameMatch = Regex.Match(body, @"\bName\s+""(?<name>[^""]+)""");
        var name = nameMatch.Success ? nameMatch.Groups["name"].Value : $"Pass{index}";

        var programMatch = Regex.Match(body, @"\b(?:HLSLPROGRAM|CGPROGRAM)\b");
        if (!programMatch.Success) return null;

        var programStart = programMatch.Index + programMatch.Length;
        var endMarker = Regex.Match(body[programStart..], @"\b(?:ENDHLSL|ENDCG)\b");
        var programEnd = endMarker.Success ? programStart + endMarker.Index : body.Length;
        var rawProgram = body[programStart..programEnd];

        var vertex = Regex.Match(rawProgram, @"#pragma\s+vertex\s+(?<entry>\w+)");
        var fragment = Regex.Match(rawProgram, @"#pragma\s+fragment\s+(?<entry>\w+)");
        if (!vertex.Success || !fragment.Success) return null;

        var pass = new ShaderLabPass
        {
            Name = name,
            LightMode = ReadTag(body, "LightMode"),
            IsOutline = name.Contains("OUTLINE", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(rawProgram, @"#define\s+LIL_OUTLINE\b"),
            VertexEntry = vertex.Groups["entry"].Value,
            FragmentEntry = fragment.Groups["entry"].Value
        };

        var states = body[..programMatch.Index];
        foreach (Match state in StateRegex.Matches(states))
        {
            var key = state.Groups["key"].Value;
            var value = state.Groups["value"].Value.Trim();
            switch (key)
            {
                case "Cull": pass.Cull = value; break;
                case "ZWrite": pass.ZWrite = value; break;
                case "ZTest": pass.ZTest = value; break;
                case "ColorMask": pass.ColorMask = value; break;
                case "AlphaToMask": pass.AlphaToMask = value; break;
                case "Blend": ParseBlend(value, pass); break;
                case "BlendOp": ParseBlendOp(value, pass); break;
                case "Offset": ParseOffset(value, pass); break;
            }
        }

        foreach (Match pragma in Regex.Matches(rawProgram,
                     @"^\s*#pragma\s+(?:shader_feature|multi_compile)(?:_local)?\s+(?<tokens>[^\r\n]+)",
                     RegexOptions.Multiline))
        {
            foreach (var token in pragma.Groups["tokens"].Value
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Trim('_').Length == 0 || !ShaderKeywordRules.IsValid(token)) continue;
                if (!pass.DefaultKeywords.Contains(token, StringComparer.Ordinal))
                    pass.DefaultKeywords.Add(token);
            }
        }

        var source = new StringBuilder();
        source.AppendLine("#include \"UnityShim.hlsl\"");
        source.AppendLine(topIncludes);
        source.AppendLine(subIncludes);
        source.AppendLine(StripPragmas(rawProgram));

        if (source.Length > MaxPassChars)
        {
            program.Warnings.Add($"Pass {name} 源码过大（超过 4 MB），已跳过。");
            program.FailureKind = "size";
            return null;
        }

        pass.Source = source.ToString();

        return pass;
    }

    private static void ParseBlend(string value, ShaderLabPass pass)
    {
        var parts = value.Split(',', 2, StringSplitOptions.TrimEntries);
        var rgb = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        pass.SrcBlend = rgb.ElementAtOrDefault(0) ?? "One";
        pass.DstBlend = rgb.ElementAtOrDefault(1) ?? pass.SrcBlend;

        if (parts.Length > 1)
        {
            var alpha = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            pass.SrcBlendAlpha = alpha.ElementAtOrDefault(0) ?? pass.SrcBlend;
            pass.DstBlendAlpha = alpha.ElementAtOrDefault(1) ?? pass.DstBlend;
        }
        else
        {
            pass.SrcBlendAlpha = pass.SrcBlend;
            pass.DstBlendAlpha = pass.DstBlend;
        }
    }

    private static void ParseBlendOp(string value, ShaderLabPass pass)
    {
        var parts = value.Split(',', 2, StringSplitOptions.TrimEntries);
        pass.BlendOp = parts[0];
        pass.BlendOpAlpha = parts.Length > 1 ? parts[1] : parts[0];
    }

    private static void ParseOffset(string value, ShaderLabPass pass)
    {
        var parts = value.Split(',', 2, StringSplitOptions.TrimEntries);
        pass.OffsetFactor = parts[0];
        pass.OffsetUnits = parts.Length > 1 ? parts[1] : "0";
    }

    private static string ReadTag(string body, string key)
    {
        var tags = Regex.Match(body, @"\bTags\s*\{(?<tags>.*?)\}", RegexOptions.Singleline);
        if (!tags.Success) return string.Empty;

        foreach (Match tag in TagRegex.Matches(tags.Groups["tags"].Value))
            if (string.Equals(tag.Groups[1].Value, key, StringComparison.OrdinalIgnoreCase))
                return tag.Groups[2].Value;

        return string.Empty;
    }

    private static List<ShaderLabProperty> ParseProperties(string block)
    {
        var result = new List<ShaderLabProperty>();
        foreach (Match match in PropertyRegex.Matches(block))
        {
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value.Trim();
            var value = match.Groups["default"].Value.Trim();
            var numbers = ParseNumbers(value);
            result.Add(new ShaderLabProperty
            {
                Name = name,
                Type = type,
                Default = value,
                Numbers = numbers
            });
        }

        return result;
    }

    private static float[] ParseNumbers(string value)
    {
        if (value.StartsWith('"')) return [];
        var matches = Regex.Matches(value, @"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?");
        var numbers = new float[matches.Count];
        for (var i = 0; i < matches.Count; i++)
            float.TryParse(matches[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]);
        return numbers;
    }

    private static string StripPragmas(string source)
    {
        return Regex.Replace(source, @"^[ \t]*#\s*pragma\b.*$", match => "// " + match.Value.Trim(),
            RegexOptions.Multiline);
    }

    private static (int, int)? FindTopLevelBlock(string text, string clean, int offset, string keyword)
    {
        var all = FindTopLevelBlocks(text, clean, offset, keyword);
        return all.Count == 0 ? null : all[0];
    }

    private static List<(int, int)> FindTopLevelBlocks(string text, string clean, int offset, params string[] keywords)
    {
        var results = new List<(int, int)>();
        var depth = 0;
        var i = 0;
        while (i < clean.Length)
        {
            var c = clean[i];
            if (c == '{')
            {
                depth++;
                i++;
                continue;
            }

            if (c == '}')
            {
                depth--;
                i++;
                continue;
            }

            if (depth == 0 && (char.IsLetter(c) || c == '_'))
            {
                var start = i;
                while (i < clean.Length && (char.IsLetterOrDigit(clean[i]) || clean[i] == '_')) i++;
                var word = clean[start..i];
                if (keywords.Contains(word, StringComparer.Ordinal))
                {
                    if (word is "CGINCLUDE" or "HLSLINCLUDE")
                    {
                        var endMarker = Regex.Match(text[(offset + i)..], @"\b(?:ENDCG|ENDHLSL)\b");
                        if (!endMarker.Success) return results;
                        results.Add((offset + i, offset + i + endMarker.Index));
                        i += endMarker.Index + endMarker.Length;
                        continue;
                    }

                    var probe = i;
                    while (probe < clean.Length && char.IsWhiteSpace(clean[probe])) probe++;
                    if (probe >= clean.Length || clean[probe] != '{')
                    {
                        continue;
                    }

                    var close = MatchBrace(clean, probe);
                    if (close < 0) return results;
                    results.Add((offset + start, offset + close + 1));
                    i = close + 1;
                    continue;
                }

                continue;
            }

            i++;
        }

        return results;
    }

    private static int MatchBrace(string text, int open)
    {
        if (open < 0 || open >= text.Length || text[open] != '{') return -1;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static string Clean(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n') chars[i++] = ' ';
            }
            else if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                chars[i++] = ' ';
                chars[i++] = ' ';
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/'))
                    chars[i++] = ' ';
                if (i < chars.Length) chars[i] = ' ';
                if (i + 1 < chars.Length) chars[i + 1] = ' ';
            }
            else if (chars[i] == '"')
            {
                chars[i++] = ' ';
                while (i < chars.Length && chars[i] != '"')
                {
                    if (chars[i] == '\n') break;
                    chars[i++] = ' ';
                }

                if (i < chars.Length && chars[i] == '"') chars[i] = ' ';
            }
        }

        return new string(chars);
    }
}
