using System.Security.Cryptography;
using System.Text;
using Akashic.Core.Services.Preview;
using StbImageSharp;
using StbImageWriteSharp;

namespace Akashic.Host.Preview;

public sealed class TexturePreviewService
{
    private const string FormatVersion = "tex-v1";

    private static readonly HashSet<string> NativeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"
    };

    private static readonly HashSet<string> DecodableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tga", ".psd", ".hdr", ".exr", ".tif", ".tiff", ".dds", ".pic", ".pnm"
    };

    private readonly string _root;

    public TexturePreviewService()
    {
        _root = PreviewCachePaths.VersionedUnder(PreviewCachePaths.Root, "textures", FormatVersion);
    }

    public static bool NeedsDecode(string path) =>
        DecodableFormats.Contains(Path.GetExtension(path));

    public static bool IsTexture(string kind) => kind == "texture";

    public (string Path, string ContentType) Resolve(string source)
    {
        var extension = Path.GetExtension(source);

        if (NativeFormats.Contains(extension))
            return (source, ContentTypeOf(extension));

        if (!DecodableFormats.Contains(extension))
            return (source, "application/octet-stream");

        var info = new FileInfo(source);
        if (!info.Exists) return (source, "application/octet-stream");

        var cache = Path.Combine(_root, KeyOf(source, info) + ".png");
        if (!File.Exists(cache))
        {
            try
            {
                DecodeToPng(source, cache);
            }
            catch (Exception)
            {
                return (source, "application/octet-stream");
            }
        }

        return (cache, "image/png");
    }

    private static void DecodeToPng(string source, string target)
    {
        using var stream = File.OpenRead(source);
        var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        var temp = target + ".tmp";
        using (var output = File.Create(temp))
        {
            new ImageWriter().WritePng(image.Data, image.Width, image.Height,
                StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        }

        File.Move(temp, target, overwrite: true);
    }

    private static string ContentTypeOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private static string KeyOf(string path, FileInfo info)
    {
        var raw = $"tex-v1|{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..20].ToLowerInvariant();
    }
}
