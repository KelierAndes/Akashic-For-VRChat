using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Downloads what the signed-in Booth account already owns. The WebView2 login owns the
/// session (Booth stores it inside the browser profile), the list pages are read from the
/// rendered DOM there, and the cookies they export are handed over so plain HTTP can follow
/// the downloadables redirect. The cookie set is never written to disk.
/// </summary>
public interface IBoothService
{
    /// <summary>
    /// The booth.pm cookies exported from the WebView2 profile, by name. Assigning replaces
    /// the previous set; Booth rotates the session value, so re-export after every login or
    /// list refresh.
    /// </summary>
    IReadOnlyDictionary<string, string>? SessionCookies { get; set; }

    bool HasSession { get; }

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

    string GetDownloadDirectory();

    Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default);

    /// <summary>
    /// Sets <see cref="BoothPurchaseItem.LocalZipPath"/> on purchases whose archive is already in
    /// the download directory, so re-reading the list keeps them marked as downloaded.
    /// </summary>
    void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items);

    /// <summary>
    /// Downloads one purchased form into the download directory and returns the zip path.
    /// </summary>
    Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Unpacks an archive next to its zip and reports what kind of Unity content it holds.
    /// </summary>
    Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default);
}
