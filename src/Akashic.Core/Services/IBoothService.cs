using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Downloads what the signed-in Booth account already owns. The WebView2 login owns the
/// session, the list pages are read from the rendered DOM there, and the cookies they export are
/// handed over so plain HTTP can follow the downloadables redirect. The exported set is stored
/// encrypted under the current Windows account so a WebView2 profile reset is not a sign-out.
/// </summary>
public interface IBoothService
{
    /// <summary>
    /// The booth.pm cookies by name, which is the shape a hand-built <c>Cookie:</c> header needs.
    /// Read-only: import through <see cref="ImportSession"/> so the domains survive.
    /// </summary>
    IReadOnlyDictionary<string, string>? SessionCookies { get; }

    /// <summary>The same cookies with their domains, for seeding a fresh WebView2 back.</summary>
    IReadOnlyList<StoredCookie>? SessionEntries { get; }

    /// <summary>
    /// Replaces the session with a fresh export. Booth rotates the session value, so re-export after
    /// every login or list refresh. An empty set is ignored rather than treated as a sign-out.
    /// </summary>
    void ImportSession(IReadOnlyList<StoredCookie> cookies);

    bool HasSession { get; }

    /// <summary>
    /// One cheap request that says whether the stored session still works: true signed in,
    /// false signed out, null when Booth could not be asked.
    /// </summary>
    Task<bool?> ProbeSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// The page the WebView2 has to open so the list extractor can run against it.
    /// </summary>
    string GetLibraryUrl(int page);

    /// <summary>
    /// The free downloads live on their own page tree under the account library; the purchase
    /// list never shows them.
    /// </summary>
    string GetFreeLibraryUrl(int page);

    /// <summary>
    /// Gifted items live on a third page tree under the account library.
    /// </summary>
    string GetGiftsUrl(int page);

    /// <summary>
    /// JavaScript that collects the download entries of an already rendered library page.
    /// </summary>
    string PurchaseExtractorScript { get; }

    /// <summary>
    /// JavaScript that reads the free-downloads library cards. Those cards carry no download
    /// control at all — only the product link, title, shop and thumbnail — so this returns
    /// product metadata whose item pages are then visited for the real downloadables.
    /// </summary>
    string FreeLibraryExtractorScript { get; }

    /// <summary>
    /// JavaScript that collects the downloadable entries of a product page, including free ones.
    /// </summary>
    string ItemPageExtractorScript { get; }

    /// <summary>
    /// Turns the result of <see cref="PurchaseExtractorScript"/> into items, tolerating the
    /// JSON-string wrapping WebView2's script execution adds.
    /// </summary>
    BoothLibraryPage ParseLibraryPage(string extractorResultJson);

    /// <summary>Turns the result of <see cref="ItemPageExtractorScript"/> into items.</summary>
    BoothLibraryPage ParseItemPage(string extractorResultJson);

    /// <summary>Turns the result of <see cref="FreeLibraryExtractorScript"/> into product metadata.</summary>
    BoothLibraryPage ParseFreeLibraryPage(string extractorResultJson);

    /// <summary>
    /// Loads the disk snapshot of the last full library read, so the page can open with a list
    /// without re-walking every library page.
    /// </summary>
    BoothLibraryCache LoadLibraryCache();

    /// <summary>Overwrites the disk snapshot with the given library read.</summary>
    Task SaveLibraryCacheAsync(IReadOnlyList<BoothPurchaseItem> items, CancellationToken ct = default);

    string GetDownloadDirectory();

    Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default);

    /// <summary>
    /// Sets <see cref="BoothPurchaseItem.LocalZipPath"/> on purchases whose archive is already in
    /// the download directory, so re-reading the list keeps them marked as downloaded.
    /// </summary>
    void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items);

    /// <summary>
    /// Removes the superseded downloadables of products that appear several times, keeping the
    /// newest of each, and reports per survivor how many versions it stands in for. One Booth
    /// product carries a bundle, a rebuild and a renamed re-upload as separate rows, so a list
    /// without this reads as a pile of duplicates.
    /// </summary>
    /// <returns>How many rows were folded away in total.</returns>
    int FoldOldVersions(List<BoothPurchaseItem> items, Dictionary<string, int> hiddenByNumber);

    /// <summary>
    /// Downloads one purchased form into the download directory and returns the zip path.
    /// </summary>
    Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Unpacks an archive next to its zip and reports what kind of Unity content it holds.
    /// </summary>
    Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default);
}
