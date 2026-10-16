using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Akashic.Core.Models;

/// <summary>
/// One backup archive sitting in the backup folder. The name carries the project and
/// the moment it was taken, and the two tools that produce these files disagree about
/// the shape, so both are read.
/// </summary>
public class BackupArchive
{
    private static readonly Regex[] Patterns =
    [
        // This app: Seconds_of_Life_20260718_122519
        new(@"^(?<name>.+)_(?<stamp>\d{8}_\d{6})$", RegexOptions.Compiled),
        // Unity VCC: EdenSpace-2026-07-13T18-20-05
        new(@"^(?<name>.+)-(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})$", RegexOptions.Compiled),
    ];

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>The project the archive claims to hold, taken from the name.</summary>
    public required string ProjectName { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Which naming scheme matched, so the list can say where a backup came from.</summary>
    public required string Source { get; init; }

    public long Size { get; init; }

    public int EntryCount { get; init; }

    public long UncompressedSize { get; init; }

    /// <summary>False when the central directory could not be read — a half-written zip.</summary>
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

    /// <summary>Loose match against a registered project, for the "which project is this" column.</summary>
    public bool LooksLike(string projectName) =>
        !string.IsNullOrWhiteSpace(projectName) &&
        (string.Equals(ProjectName, projectName, StringComparison.OrdinalIgnoreCase) ||
         ProjectName.Replace('_', ' ').Equals(projectName, StringComparison.OrdinalIgnoreCase) ||
         projectName.Replace(' ', '_').Equals(ProjectName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when the file is not a backup this app can name.</summary>
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

        // A zip in the backup folder with an unrecognised name is still restorable;
        // only its date and owner have to come from the file system.
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

    /// <summary>Reads only the central directory, so a multi-gigabyte archive is cheap to list.</summary>
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
