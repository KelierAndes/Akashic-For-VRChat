using System.IO.Compression;
using System.Text;
using Akashic.Core.Models;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;

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
            using var gzipStream = new GZipInputStream(fileStream);
            using var tarReader = new TarInputStream(gzipStream, Encoding.UTF8) { IsStreamOwner = false };

            TarEntry? tarEntry;
            while ((tarEntry = tarReader.GetNextEntry()) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (tarEntry.IsDirectory || string.IsNullOrEmpty(tarEntry.Name)) continue;

                var entryName = tarEntry.Name.Replace('\\', '/');
                while (entryName.StartsWith("./", StringComparison.Ordinal))
                    entryName = entryName[2..];

                var parts = entryName.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var guid = parts[0];
                var fileName = parts[1];

                if (!guidEntries.TryGetValue(guid, out var entry))
                {
                    entry = new UnityPackageEntry { Guid = guid };
                    guidEntries[guid] = entry;
                }

                var data = ReadPayload(tarReader, tarEntry.Size, ct);
                if (data.Length == 0) continue;

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

    private static byte[] ReadPayload(TarInputStream reader, long size, CancellationToken ct)
    {
        if (size <= 0 || size > int.MaxValue) return [];

        var buffer = new byte[Math.Min(size, 81920)];
        using var stream = new MemoryStream((int)size);
        var remaining = size;

        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = (int)Math.Min(remaining, buffer.Length);
            var read = reader.Read(buffer, 0, chunk);
            if (read <= 0) break;

            stream.Write(buffer, 0, read);
            remaining -= read;
        }

        return stream.ToArray();
    }

    private static string ReadPathName(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);

        var lineBreak = text.IndexOfAny(['\n', '\r', '\0']);
        if (lineBreak >= 0) text = text[..lineBreak];

        return text.Trim().Replace('\\', '/').TrimEnd('/');
    }
}
