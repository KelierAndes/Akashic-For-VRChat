using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

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
