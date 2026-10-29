using System.Diagnostics;

namespace Akashic.Host.Preview.Hlsl;

public sealed class FxcTool
{
    private FxcTool(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static FxcTool? Find()
    {
        var candidates = new List<string>
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "fxc", "fxc.exe"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Akashic.Host", "tools", "fxc", "fxc.exe")
        };

        var kits = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        if (Directory.Exists(kits))
        {
            candidates.AddRange(Directory.GetDirectories(kits)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(version => System.IO.Path.Combine(version, "x64", "fxc.exe")));
        }

        foreach (var candidate in candidates)
        {
            var full = System.IO.Path.GetFullPath(candidate);
            if (File.Exists(full)) return new FxcTool(full);
        }

        return null;
    }

    public async Task<(bool Ok, bool TimedOut, string Log)> CompileAsync(string sourcePath, string entry,
        string profile, string outputPath, IEnumerable<string> includes, IEnumerable<string> defines,
        TimeSpan timeout, CancellationToken ct)
    {
        var arguments = new List<string>
        {
            "/nologo", "/T", profile, "/E", entry, "/Gec", "/O1", "/Fo", outputPath
        };

        foreach (var include in includes.Distinct(StringComparer.OrdinalIgnoreCase))
            arguments.Add("/I" + include);

        foreach (var define in defines.Distinct(StringComparer.Ordinal))
            arguments.Add("/D" + define);

        arguments.Add(sourcePath);

        var start = new ProcessStartInfo
        {
            FileName = Path,
            WorkingDirectory = System.IO.Path.GetDirectoryName(sourcePath) ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start);
            if (process is null) return (false, false, "无法启动 fxc。");

            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return (false, !ct.IsCancellationRequested, "着色器编译超时。");
            }

            var log = (await output + await error).Trim();
            var ok = process.ExitCode == 0 && File.Exists(outputPath);
            return (ok, false, log);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return (false, false, ex.Message);
        }
    }
}
