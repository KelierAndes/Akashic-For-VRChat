using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// Who supplied a package, which is separate from how it reached the library: a Booth archive and a
/// hand-picked file can both end up as the same kind of .unitypackage entry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageOrigin
{
    Local,
    Booth,
    AssetStore
}

public static class PackageOriginExtensions
{
    public static string Display(this PackageOrigin origin) => origin switch
    {
        PackageOrigin.Booth => "Booth 导入",
        PackageOrigin.AssetStore => "Asset Store",
        _ => "本地"
    };
}
