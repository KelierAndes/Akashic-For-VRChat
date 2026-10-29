using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Akashic.Core.Services.Preview;

public static class AssimpPaths
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferLength);

    public static string Openable(string path)
    {
        if (IsAscii(path)) return path;

        var shortPath = Short(path);
        if (!string.IsNullOrEmpty(shortPath) && IsAscii(shortPath)) return shortPath!;

        return Copy(path);
    }

    private static string? Short(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    private static string Copy(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "akashic-models");
        Directory.CreateDirectory(root);

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
        var target = Path.Combine(root, key[..24] + Path.GetExtension(path));

        if (!File.Exists(target)) File.Copy(path, target, overwrite: true);
        return target;
    }

    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character > 127) return false;
        }

        return true;
    }
}
