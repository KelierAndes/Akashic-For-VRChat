using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IVRChatBlueprintScanService
{
    Task<IReadOnlyList<string>> ScanAsync(UnityProject project, bool force = false, CancellationToken ct = default);

    void PublishCloudIds(IEnumerable<string> blueprintIds);

    IReadOnlyList<string> CloudIds { get; }

    Task<Dictionary<string, UnityProject>> MapByBlueprintAsync(
        IReadOnlyList<UnityProject> projects, CancellationToken ct = default);
}
