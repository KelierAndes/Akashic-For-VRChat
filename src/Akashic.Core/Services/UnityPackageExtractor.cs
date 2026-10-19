using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public class UnityPackageExtractor : IUnityPackageExtractor
{
    public async Task<UnityPackageArchive> ParseAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Unity package file not found.", filePath);

        var archive = new UnityPackageArchive
        {
            FilePath = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath)
        };

        var guidEntries = new Dictionary<string, UnityPackageEntry>();

        await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(filePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);

            TarEntry? tarEntry;
            while ((tarEntry = tarReader.GetNextEntry()) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (tarEntry.EntryType == TarEntryType.Directory) continue;

                var entryName = tarEntry.Name.Replace('\\', '/');
                var parts = entryName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var guid = parts[0];
                var fileName = parts[1];

                if (!guidEntries.TryGetValue(guid, out var entry))
                {
                    entry = new UnityPackageEntry { Guid = guid };
                    guidEntries[guid] = entry;
                }

                if (tarEntry.DataStream == null) continue;

                using var ms = new MemoryStream();
                tarEntry.DataStream.CopyTo(ms);
                var data = ms.ToArray();

                switch (fileName)
                {
                    case "pathname":
                        entry.PathName = ReadPathName(data);
                        break;
                    case "asset":
                        entry.AssetData = data;
                        break;
                    case "asset.meta":
                        entry.MetaContent = Encoding.UTF8.GetString(data);
                        break;
                    case "preview.png":
                        entry.PreviewData = data;
                        break;
                }
            }
        }, ct);

        archive.Entries = guidEntries.Values
            .Where(e => !string.IsNullOrEmpty(e.PathName))
            .OrderBy(e => e.PathName, StringComparer.Ordinal)
            .ToList();

        return archive;
    }

    private static string ReadPathName(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        var lineBreak = text.IndexOfAny(['\n', '\r', '\0']);
        if (lineBreak >= 0) text = text[..lineBreak];

        return text.Trim().Replace('\\', '/').TrimEnd('/');
    }
}
