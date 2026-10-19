using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IBoothService
{
    IReadOnlyDictionary<string, string>? SessionCookies { get; }

    IReadOnlyList<StoredCookie>? SessionEntries { get; }

    void ImportSession(IReadOnlyList<StoredCookie> cookies);

    bool HasSession { get; }

    Task<bool?> ProbeSessionAsync(CancellationToken ct = default);

    string GetLibraryUrl(int page);

    string GetFreeLibraryUrl(int page);

    string GetGiftsUrl(int page);

    string PurchaseExtractorScript { get; }

    string FreeLibraryExtractorScript { get; }

    string ItemPageExtractorScript { get; }

    BoothLibraryPage ParseLibraryPage(string extractorResultJson);

    BoothLibraryPage ParseItemPage(string extractorResultJson);

    BoothLibraryPage ParseFreeLibraryPage(string extractorResultJson);

    BoothLibraryCache LoadLibraryCache();

    Task SaveLibraryCacheAsync(IReadOnlyList<BoothPurchaseItem> items, CancellationToken ct = default);

    string GetDownloadDirectory();

    Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default);

    void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items);

    int FoldOldVersions(List<BoothPurchaseItem> items, Dictionary<string, int> hiddenByNumber);

    Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null, CancellationToken ct = default);

    Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default);
}
