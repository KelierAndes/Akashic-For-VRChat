using System.Text;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services.Preview.Shader;

public sealed class ShaderIncludeResult
{
    public string Source { get; init; } = string.Empty;

    public List<string> Rejected { get; init; } = [];

    public List<string> Missing { get; init; } = [];

    public bool TooLarge { get; init; }

    public bool Ok => Rejected.Count == 0 && !TooLarge;
}

public static class ShaderIncludeResolver
{
    public const int MaxSourceChars = 8 * 1024 * 1024;

    public const int MaxDepth = 8;

    private static readonly Regex IncludeRegex = new(
        "^[ \t]*#[ \t]*include[ \t]*(?<open>[\"<])(?<path>[^\"\r\n]+?)[\">]",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static ShaderIncludeResult Expand(string source, IReadOnlyList<string> roots)
    {
        var state = new State();
        ExpandInto(source, roots, null, 0, state);

        return new ShaderIncludeResult
        {
            Source = state.Output.ToString(),
            Rejected = state.Rejected,
            Missing = state.Missing,
            TooLarge = state.TooLarge
        };
    }

    private sealed class State
    {
        public StringBuilder Output { get; } = new();

        public List<string> Rejected { get; } = [];

        public List<string> Missing { get; } = [];

        public HashSet<string> Chain { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TooLarge { get; set; }
    }

    private static void ExpandInto(string source, IReadOnlyList<string> roots, string? currentDirectory,
        int depth, State state)
    {
        if (state.TooLarge) return;

        var bases = currentDirectory is null ? roots : [currentDirectory, .. roots];

        var position = 0;
        foreach (Match match in IncludeRegex.Matches(source))
        {
            state.Output.Append(source, position, match.Index - position);
            position = match.Index + match.Length;
            state.Output.AppendLine();

            var path = match.Groups["path"].Value.Trim().Replace('\\', '/');
            var resolved = Resolve(path, bases, roots);

            if (resolved.Status == ResolveStatus.Rejected || depth >= MaxDepth)
            {
                state.Rejected.Add(path);
                state.Output.AppendLine("// akashic: include rejected");
                continue;
            }

            if (resolved.Status == ResolveStatus.Missing)
            {
                state.Missing.Add(path);
                state.Output.AppendLine(match.Value.Trim());
                continue;
            }

            var full = resolved.Path!;
            if (!state.Chain.Add(full))
            {
                state.Output.AppendLine("// akashic: include cycle");
                continue;
            }

            try
            {
                ExpandInto(File.ReadAllText(full), roots, Path.GetDirectoryName(full), depth + 1, state);
            }
            catch (IOException)
            {
                state.Missing.Add(path);
            }
            catch (UnauthorizedAccessException)
            {
                state.Missing.Add(path);
            }
            finally
            {
                state.Chain.Remove(full);
            }

            if (state.Output.Length > MaxSourceChars)
            {
                state.TooLarge = true;
                return;
            }
        }

        state.Output.Append(source, position, source.Length - position);
        if (state.Output.Length > MaxSourceChars) state.TooLarge = true;
    }

    private enum ResolveStatus
    {
        Found,
        Missing,
        Rejected
    }

    private static (ResolveStatus Status, string? Path) Resolve(string path, IReadOnlyList<string> bases,
        IReadOnlyList<string> allowedRoots)
    {
        if (path.Length == 0) return (ResolveStatus.Missing, null);
        if (Path.IsPathRooted(path)) return (ResolveStatus.Rejected, null);

        var escapedEveryBase = true;

        foreach (var baseDirectory in bases)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) continue;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(baseDirectory, path));
            }
            catch (Exception)
            {
                continue;
            }

            if (!allowedRoots.Any(root => Inside(candidate, root))) continue;

            escapedEveryBase = false;
            if (File.Exists(candidate)) return (ResolveStatus.Found, candidate);
        }

        return escapedEveryBase ? (ResolveStatus.Rejected, null) : (ResolveStatus.Missing, null);
    }

    private static bool Inside(string candidate, string root)
    {
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception)
        {
            return false;
        }

        return candidate.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase);
    }
}
