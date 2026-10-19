using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Akashic.Core.Models;

public class BackupArchive
{
    private static readonly Regex[] Patterns =
    [
        new(@"^(?<name>.+)_(?<stamp>\d{8}_\d{6})$", RegexOptions.Compiled),
        new(@"^(?<name>.+)-(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})$", RegexOptions.Compiled),
    ];

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string ProjectName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required string Source { get; init; }

    public long Size { get; init; }

    public int EntryCount { get; init; }

    public long UncompressedSize { get; init; }

    public bool Readable { get; init; }

    public string SizeDisplay => Size >= 1073741824
        ? $"{(double)Size / 1073741824:F2} GB"
        : Size >= 1048576
            ? $"{(double)Size / 1048576:F1} MB"
            : $"{Size / 1024.0:F1} KB";

    public string ContentDisplay => UncompressedSize >= 1073741824
        ? $"{EntryCount} 个文件 · 解压后 {(double)UncompressedSize / 1073741824:F2} GB"
        : $"{EntryCount} 个文件 · 解压后 {(double)UncompressedSize / 1048576:F1} MB";

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy/MM/dd HH:mm");

    public bool LooksLike(string projectName) =>
        !string.IsNullOrWhiteSpace(projectName) &&
        (string.Equals(ProjectName, projectName, StringComparison.OrdinalIgnoreCase) ||
         ProjectName.Replace('_', ' ').Equals(projectName, StringComparison.OrdinalIgnoreCase) ||
         projectName.Replace(' ', '_').Equals(ProjectName, StringComparison.OrdinalIgnoreCase));

    public static BackupArchive? TryParse(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || !string.Equals(info.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
            return null;

        var stem = Path.GetFileNameWithoutExtension(info.Name);

        foreach (var (pattern, source, format) in Zip(Patterns, ["本应用", "Unity VCC"],
                     ["yyyyMMdd_HHmmss", "yyyy-MM-ddTHH-mm-ss"]))
        {
            var match = pattern.Match(stem);
            if (!match.Success) continue;

            var created = DateTime.TryParseExact(match.Groups["stamp"].Value, format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : info.LastWriteTime;

            var (entries, bytes, readable) = ReadIndex(path);

            return new BackupArchive
            {
                FilePath = path,
                FileName = info.Name,
                ProjectName = match.Groups["name"].Value,
                CreatedAt = created,
                Source = source,
                Size = info.Length,
                EntryCount = entries,
                UncompressedSize = bytes,
                Readable = readable
            };
        }

        var (fallbackEntries, fallbackBytes, fallbackReadable) = ReadIndex(path);
        return new BackupArchive
        {
            FilePath = path,
            FileName = info.Name,
            ProjectName = stem,
            CreatedAt = info.LastWriteTime,
            Source = "其他",
            Size = info.Length,
            EntryCount = fallbackEntries,
            UncompressedSize = fallbackBytes,
            Readable = fallbackReadable
        };
    }

    private static IEnumerable<(Regex, string, string)> Zip(Regex[] regexes, string[] sources, string[] formats)
    {
        for (var i = 0; i < regexes.Length; i++)
            yield return (regexes[i], sources[i], formats[i]);
    }

    private static (int Entries, long Bytes, bool Readable) ReadIndex(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            long bytes = 0;
            foreach (var entry in zip.Entries)
                bytes += entry.Length;
            return (zip.Entries.Count, bytes, true);
        }
        catch (Exception)
        {
            return (0, 0, false);
        }
    }
}
