using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Reads the blueprint ids the VRChat SDK stamps into a project's scenes and pairs them with the ids
/// the account holds in the cloud, so either side can link to the other.
/// </summary>
public interface IVRChatBlueprintScanService
{
    /// <summary>Blueprint ids this project's scenes produce; cached per scene until it changes.</summary>
    Task<IReadOnlyList<string>> ScanAsync(UnityProject project, bool force = false, CancellationToken ct = default);

    /// <summary>Records the ids the cloud list currently holds, which is everything matching runs against.</summary>
    void PublishCloudIds(IEnumerable<string> blueprintIds);

    /// <summary>The cloud ids published so far.</summary>
    IReadOnlyList<string> CloudIds { get; }

    /// <summary>Blueprint id → the project whose scenes produce it, across every registered project.</summary>
    Task<Dictionary<string, UnityProject>> MapByBlueprintAsync(
        IReadOnlyList<UnityProject> projects, CancellationToken ct = default);
}
