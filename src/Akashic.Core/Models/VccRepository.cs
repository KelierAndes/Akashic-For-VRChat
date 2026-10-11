namespace Akashic.Core.Models;

/// <summary>
/// A VPM repository as VCC knows it: the URL it is served from and where its listing is cached on disk.
/// </summary>
public class VccRepository
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}
