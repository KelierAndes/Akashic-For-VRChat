using System.Globalization;
using System.Text.Json;
using Akashic.Core.Models;
using Microsoft.Data.Sqlite;

namespace Akashic.Core.Data;

public sealed record CachedProject(string Signature, UnityProject Project);

public sealed partial class AkashicDatabase
{
    public List<UnityProject> LoadProjects()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM projects ORDER BY name COLLATE NOCASE";
        using var reader = command.ExecuteReader();

        var projects = new List<UnityProject>();
        while (reader.Read())
        {
            var project = Deserialize<UnityProject>(reader.GetString(0));
            if (project is not null) projects.Add(project);
        }
        reader.Close();

        foreach (var project in projects) FillInstalledPackages(connection, project);
        return projects;
    }

    public Dictionary<string, CachedProject> LoadProjectCache()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, signature, payload FROM projects";
        using var reader = command.ExecuteReader();

        var rows = new List<(string Path, string Signature, UnityProject Project)>();
        while (reader.Read())
        {
            var project = Deserialize<UnityProject>(reader.GetString(2));
            if (project is not null) rows.Add((reader.GetString(0), reader.GetString(1), project));
        }
        reader.Close();

        var result = new Dictionary<string, CachedProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, signature, project) in rows)
        {
            FillInstalledPackages(connection, project);
            result[path] = new CachedProject(signature, project);
        }

        return result;
    }

    public void UpsertProject(string signature, UnityProject project)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO projects(path, name, kind, unity_version, last_modified, package_count, signature, payload)
                VALUES($path, $name, $kind, $unity, $modified, $count, $signature, $payload)
                ON CONFLICT(path) DO UPDATE SET
                    name = excluded.name,
                    kind = excluded.kind,
                    unity_version = excluded.unity_version,
                    last_modified = excluded.last_modified,
                    package_count = excluded.package_count,
                    signature = excluded.signature,
                    payload = excluded.payload
                """;
            command.Parameters.AddWithValue("$path", project.Path);
            command.Parameters.AddWithValue("$name", project.Name);
            command.Parameters.AddWithValue("$kind", project.ProjectTypeDisplayName);
            command.Parameters.AddWithValue("$unity", project.UnityVersion);
            command.Parameters.AddWithValue("$modified", ToUnix(project.LastModified));
            command.Parameters.AddWithValue("$count", project.InstalledPackages.Count);
            command.Parameters.AddWithValue("$signature", signature);
            command.Parameters.AddWithValue("$payload", Serialize(project));
            command.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM installed_packages WHERE project_path = $path";
            delete.Parameters.AddWithValue("$path", project.Path);
            delete.ExecuteNonQuery();
        }

        foreach (var package in project.InstalledPackages)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO installed_packages(project_path, name, version, display_name, source)
                VALUES($path, $name, $version, $display, $source)
                ON CONFLICT(project_path, name) DO UPDATE SET
                    version = excluded.version,
                    display_name = excluded.display_name,
                    source = excluded.source
                """;
            insert.Parameters.AddWithValue("$path", project.Path);
            insert.Parameters.AddWithValue("$name", package.Name);
            insert.Parameters.AddWithValue("$version", package.Version);
            insert.Parameters.AddWithValue("$display", package.DisplayName);
            insert.Parameters.AddWithValue("$source", package.Source.ToString());
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void RemoveProject(string path)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM projects WHERE path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM installed_packages WHERE project_path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void PruneProjects(IReadOnlyCollection<string> keepPaths)
    {
        var keep = new HashSet<string>(keepPaths, StringComparer.OrdinalIgnoreCase);
        var stale = LoadProjectCache().Keys.Where(path => !keep.Contains(path)).ToList();
        foreach (var path in stale) RemoveProject(path);
    }

    public List<string> FindProjectsWithPackage(string packageName)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT DISTINCT project_path FROM installed_packages WHERE name = $name COLLATE NOCASE";
        command.Parameters.AddWithValue("$name", packageName);
        using var reader = command.ExecuteReader();

        var paths = new List<string>();
        while (reader.Read()) paths.Add(reader.GetString(0));
        return paths;
    }

    private void FillInstalledPackages(SqliteConnection connection, UnityProject project)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, version, display_name, source FROM installed_packages WHERE project_path = $path ORDER BY name";
        command.Parameters.AddWithValue("$path", project.Path);
        using var reader = command.ExecuteReader();

        var packages = new List<InstalledPackage>();
        while (reader.Read())
        {
            packages.Add(new InstalledPackage
            {
                Name = reader.GetString(0),
                Version = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? reader.GetString(0) : reader.GetString(2),
                Source = Enum.TryParse<PackageSource>(reader.IsDBNull(3) ? null : reader.GetString(3), true, out var source)
                    ? source
                    : PackageSource.Unknown
            });
        }

        if (packages.Count > 0) project.InstalledPackages = packages;
    }

    public DateTime? GetVpmFetchedAt(string source)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT fetched_at FROM vpm_sources WHERE source = $source";
        command.Parameters.AddWithValue("$source", source);
        var value = command.ExecuteScalar();
        if (value is null or DBNull) return null;
        return FromUnix(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    public List<VpmPackage> LoadVpmPackages(string source)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, source_name, payload FROM vpm_packages WHERE source = $source ORDER BY name COLLATE NOCASE";
        command.Parameters.AddWithValue("$source", source);
        using var reader = command.ExecuteReader();

        var packages = new List<VpmPackage>();
        while (reader.Read())
        {
            var package = Deserialize<VpmPackage>(reader.GetString(2));
            if (package is null) continue;
            if (string.IsNullOrEmpty(package.Name)) package.Name = reader.GetString(0);
            package.SourceRegistry = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            package.SourceRegistryUrl = source;
            packages.Add(package);
        }

        return packages;
    }

    public void ReplaceVpmPackages(string source, string? sourceName, IReadOnlyList<VpmPackage> packages,
        string? error = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM vpm_packages WHERE source = $source";
            delete.Parameters.AddWithValue("$source", source);
            delete.ExecuteNonQuery();
        }

        foreach (var package in packages)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO vpm_packages(source, source_name, name, display_name, latest, payload)
                VALUES($source, $sourceName, $name, $display, $latest, $payload)
                ON CONFLICT(source, name) DO UPDATE SET
                    source_name = excluded.source_name,
                    display_name = excluded.display_name,
                    latest = excluded.latest,
                    payload = excluded.payload
                """;
            insert.Parameters.AddWithValue("$source", source);
            insert.Parameters.AddWithValue("$sourceName", (object?)sourceName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$name", package.Name);
            insert.Parameters.AddWithValue("$display", package.DisplayName);
            insert.Parameters.AddWithValue("$latest", package.Version);
            insert.Parameters.AddWithValue("$payload", Serialize(package));
            insert.ExecuteNonQuery();
        }

        using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = """
                INSERT INTO vpm_sources(source, fetched_at, package_count, error)
                VALUES($source, $fetched, $count, $error)
                ON CONFLICT(source) DO UPDATE SET
                    fetched_at = excluded.fetched_at,
                    package_count = excluded.package_count,
                    error = excluded.error
                """;
            meta.Parameters.AddWithValue("$source", source);
            meta.Parameters.AddWithValue("$fetched", ToUnix(DateTime.Now));
            meta.Parameters.AddWithValue("$count", packages.Count);
            meta.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            meta.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearVpmPackages()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vpm_packages; DELETE FROM vpm_sources;";
        command.ExecuteNonQuery();
    }

    public List<ResourceRow> LoadResources(string? origin = null, string? category = null, string? query = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(origin) && origin != "全部")
        {
            filters.Add("origin = $origin");
            command.Parameters.AddWithValue("$origin", origin);
        }

        if (!string.IsNullOrWhiteSpace(category) && category != "全部分类")
        {
            filters.Add("category = $category");
            command.Parameters.AddWithValue("$category", category);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filters.Add("(name LIKE $query OR meta_key LIKE $query)");
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        command.CommandText =
            "SELECT payload FROM resources" +
            (filters.Count > 0 ? " WHERE " + string.Join(" AND ", filters) : string.Empty) +
            " ORDER BY last_modified DESC, name COLLATE NOCASE";
        using var reader = command.ExecuteReader();

        var rows = new List<ResourceRow>();
        while (reader.Read())
        {
            var row = Deserialize<ResourceRow>(reader.GetString(0));
            if (row is not null) rows.Add(row);
        }

        return rows;
    }

    public ResourceRow? GetResource(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM resources WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return Deserialize<ResourceRow>(command.ExecuteScalar() as string);
    }

    public void ReplaceResources(IReadOnlyList<ResourceRow> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM resources";
            delete.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO resources(key, meta_key, name, origin, category, last_modified, matched, payload)
                VALUES($key, $meta, $name, $origin, $category, $modified, $matched, $payload)
                """;
            insert.Parameters.AddWithValue("$key", row.Key);
            insert.Parameters.AddWithValue("$meta", row.MetaKey);
            insert.Parameters.AddWithValue("$name", row.Name);
            insert.Parameters.AddWithValue("$origin", row.Origin);
            insert.Parameters.AddWithValue("$category", row.Category);
            insert.Parameters.AddWithValue("$modified", ToUnix(row.LastModified));
            insert.Parameters.AddWithValue("$matched", row.Matched ? 1 : 0);
            insert.Parameters.AddWithValue("$payload", Serialize(row));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public Dictionary<string, ResourceMetaEntry> LoadResourceMeta()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT meta_key, payload FROM resource_meta";
        using var reader = command.ExecuteReader();

        var entries = new Dictionary<string, ResourceMetaEntry>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var entry = Deserialize<ResourceMetaEntry>(reader.GetString(1));
            if (entry is not null) entries[reader.GetString(0)] = entry;
        }

        return entries;
    }

    public void UpsertResourceMeta(string key, ResourceMetaEntry entry)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO resource_meta(meta_key, category, payload)
            VALUES($key, $category, $payload)
            ON CONFLICT(meta_key) DO UPDATE SET
                category = excluded.category,
                payload = excluded.payload
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$payload", Serialize(entry));
        command.ExecuteNonQuery();
    }

    public void DeleteResourceMeta(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM resource_meta WHERE meta_key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    public void ReplaceResourceMeta(IReadOnlyDictionary<string, ResourceMetaEntry> entries)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM resource_meta";
            delete.ExecuteNonQuery();
        }

        foreach (var (key, entry) in entries)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO resource_meta(meta_key, category, payload) VALUES($key, $category, $payload)";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$category", entry.Category);
            insert.Parameters.AddWithValue("$payload", Serialize(entry));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public UnityPackageFileInfo? GetPackageFile(string path, long size, DateTime modified)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM package_files WHERE path = $path AND size = $size AND modified = $modified";
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));
        var payload = command.ExecuteScalar() as string;
        return Deserialize<UnityPackageFileInfo>(payload);
    }

    public void PutPackageFile(UnityPackageFileInfo info)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO package_files(path, size, modified, payload)
            VALUES($path, $size, $modified, $payload)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                modified = excluded.modified,
                payload = excluded.payload
            """;
        command.Parameters.AddWithValue("$path", info.FilePath);
        command.Parameters.AddWithValue("$size", info.Size);
        command.Parameters.AddWithValue("$modified", ToUnix(info.LastModified));
        command.Parameters.AddWithValue("$payload", Serialize(info));
        command.ExecuteNonQuery();
    }

    public ResourceRow? GetArchiveRow(string path, long size, DateTime modified)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM archive_files WHERE path = $path AND size = $size AND modified = $modified";
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));
        var payload = command.ExecuteScalar() as string;
        var row = Deserialize<ResourceRow>(payload);
        if (row is not null) row.FilePath = path;
        return row;
    }

    public void PutArchiveRow(ResourceRow row)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archive_files(path, size, modified, payload)
            VALUES($path, $size, $modified, $payload)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                modified = excluded.modified,
                payload = excluded.payload
            """;
        command.Parameters.AddWithValue("$path", row.Key);
        command.Parameters.AddWithValue("$size", row.Size);
        command.Parameters.AddWithValue("$modified", ToUnix(row.LastModified));
        command.Parameters.AddWithValue("$payload", Serialize(row));
        command.ExecuteNonQuery();
    }

    public bool TryGetPreviewMaterial(string cacheKey, long size, DateTime modified, out PreviewMaterial? material)
    {
        material = null;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT payload FROM preview_materials WHERE cache_key = $key AND size = $size AND modified = $modified";
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));

        var payload = command.ExecuteScalar() as string;
        if (payload is null) return false;

        material = Deserialize<PreviewMaterial>(payload);
        return material is not null;
    }

    public void PutPreviewMaterial(string cacheKey, long size, DateTime modified, PreviewMaterial material)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO preview_materials(cache_key, size, modified, payload)
            VALUES($key, $size, $modified, $payload)
            ON CONFLICT(cache_key) DO UPDATE SET
                size = excluded.size,
                modified = excluded.modified,
                payload = excluded.payload
            """;
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));
        command.Parameters.AddWithValue("$payload", Serialize(material));
        command.ExecuteNonQuery();
    }

    public bool TryGetThumbnailRecord(string path, long size, DateTime modified, out string? thumbnailPath)
    {
        thumbnailPath = null;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT thumbnail_path FROM thumbnail_cache WHERE path = $path AND size = $size AND modified = $modified";
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));

        var value = command.ExecuteScalar();
        if (value is null) return false;

        thumbnailPath = value is DBNull ? null : value as string;
        return true;
    }

    public void PutThumbnailRecord(string path, long size, DateTime modified, string? thumbnailPath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thumbnail_cache(path, size, modified, thumbnail_path)
            VALUES($path, $size, $modified, $thumbnail)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                modified = excluded.modified,
                thumbnail_path = excluded.thumbnail_path
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$modified", ToUnix(modified));
        command.Parameters.AddWithValue("$thumbnail", (object?)thumbnailPath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void PruneScanCaches(IReadOnlyCollection<string> packagePaths, IReadOnlyCollection<string> archivePaths)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        DeleteMissing(connection, transaction, "package_files", "path", packagePaths);
        DeleteMissing(connection, transaction, "archive_files", "path", archivePaths);

        transaction.Commit();
    }

    private static void DeleteMissing(SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, IReadOnlyCollection<string> keep)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        var stale = new List<string>();

        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {column} FROM {table}";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!keepSet.Contains(path)) stale.Add(path);
            }
        }

        foreach (var path in stale)
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {table} WHERE {column} = $path";
            delete.Parameters.AddWithValue("$path", path);
            delete.ExecuteNonQuery();
        }
    }

    public BoothLibraryCache LoadBoothCache()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT number, payload FROM booth_items ORDER BY category, title";
        using var reader = command.ExecuteReader();

        var items = new List<BoothPurchaseItem>();
        while (reader.Read())
        {
            var item = Deserialize<BoothPurchaseItem>(reader.GetString(1));
            if (item is null) continue;
            if (string.IsNullOrEmpty(item.Number)) item.Number = reader.GetString(0);
            items.Add(item);
        }

        var savedAt = GetCacheDate("booth.savedAt");
        return new BoothLibraryCache { SavedAt = savedAt, Items = items };
    }

    public void SaveBoothCache(IReadOnlyList<BoothPurchaseItem> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM booth_items";
            delete.ExecuteNonQuery();
        }

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO booth_items(number, title, shop, category, is_free, payload)
                VALUES($number, $title, $shop, $category, $free, $payload)
                ON CONFLICT(number) DO UPDATE SET
                    title = excluded.title,
                    shop = excluded.shop,
                    category = excluded.category,
                    is_free = excluded.is_free,
                    payload = excluded.payload
                """;
            insert.Parameters.AddWithValue("$number", item.Number);
            insert.Parameters.AddWithValue("$title", item.Title);
            insert.Parameters.AddWithValue("$shop", item.Shop);
            insert.Parameters.AddWithValue("$category", item.Category);
            insert.Parameters.AddWithValue("$free", item.IsFree ? 1 : 0);
            insert.Parameters.AddWithValue("$payload", Serialize(item));
            insert.ExecuteNonQuery();
        }

        SetCacheDate(connection, transaction, "booth.savedAt", DateTime.Now);
        transaction.Commit();
    }

    public void UpdateBoothCategories(IReadOnlyDictionary<string, string> categories)
    {
        if (categories.Count == 0) return;

        var cache = LoadBoothCache();
        var changed = false;
        foreach (var item in cache.Items)
        {
            if (!categories.TryGetValue(item.Number, out var category) || category.Length == 0) continue;
            if (string.Equals(item.Category, category, StringComparison.Ordinal)) continue;
            item.Category = category;
            changed = true;
        }

        if (changed) SaveBoothCache(cache.Items);
    }

    public AssetStoreAccountCache LoadAssetStoreAccount()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM assetstore_items ORDER BY name COLLATE NOCASE";
        using var reader = command.ExecuteReader();

        var items = new List<AssetStoreAsset>();
        while (reader.Read())
        {
            var item = Deserialize<AssetStoreAsset>(reader.GetString(0));
            if (item is not null) items.Add(item);
        }

        return new AssetStoreAccountCache { SavedAt = GetCacheDate("assetstore.savedAt"), Items = items };
    }

    public void SaveAssetStoreAccount(IReadOnlyList<AssetStoreAsset> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM assetstore_items";
            delete.ExecuteNonQuery();
        }

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO assetstore_items(product_key, name, publisher, payload)
                VALUES($key, $name, $publisher, $payload)
                ON CONFLICT(product_key) DO UPDATE SET
                    name = excluded.name,
                    publisher = excluded.publisher,
                    payload = excluded.payload
                """;
            insert.Parameters.AddWithValue("$key", AssetStoreKey(item));
            insert.Parameters.AddWithValue("$name", item.Name);
            insert.Parameters.AddWithValue("$publisher", item.Publisher);
            insert.Parameters.AddWithValue("$payload", Serialize(item));
            insert.ExecuteNonQuery();
        }

        SetCacheDate(connection, transaction, "assetstore.savedAt", DateTime.Now);
        transaction.Commit();
    }

    public Dictionary<string, string> LoadAssetStoreCovers()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT match_key, url FROM assetstore_covers";
        using var reader = command.ExecuteReader();

        var covers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) covers[reader.GetString(0)] = reader.GetString(1);
        return covers;
    }

    public void SaveAssetStoreCover(string key, string url)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assetstore_covers(match_key, url) VALUES($key, $url)
            ON CONFLICT(match_key) DO UPDATE SET url = excluded.url
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$url", url);
        command.ExecuteNonQuery();
    }

    public void ReplaceAssetStoreCovers(IReadOnlyDictionary<string, string> covers)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM assetstore_covers";
            delete.ExecuteNonQuery();
        }

        foreach (var (key, url) in covers)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO assetstore_covers(match_key, url) VALUES($key, $url)";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$url", url);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public VRChatContentCache LoadVrchatContent()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM vrchat_items ORDER BY kind, name COLLATE NOCASE";
        using var reader = command.ExecuteReader();

        var items = new List<VRChatContentItem>();
        while (reader.Read())
        {
            var item = Deserialize<VRChatContentItem>(reader.GetString(0));
            if (item is not null) items.Add(item);
        }

        return new VRChatContentCache { SavedAt = GetCacheDate("vrchat.savedAt"), Items = items };
    }

    public void SaveVrchatContent(IReadOnlyList<VRChatContentItem> items)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM vrchat_items";
            delete.ExecuteNonQuery();
        }

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO vrchat_items(id, kind, name, payload)
                VALUES($id, $kind, $name, $payload)
                ON CONFLICT(id) DO UPDATE SET
                    kind = excluded.kind,
                    name = excluded.name,
                    payload = excluded.payload
                """;
            insert.Parameters.AddWithValue("$id", item.Id);
            insert.Parameters.AddWithValue("$kind", item.Kind.ToString());
            insert.Parameters.AddWithValue("$name", item.Name);
            insert.Parameters.AddWithValue("$payload", Serialize(item));
            insert.ExecuteNonQuery();
        }

        SetCacheDate(connection, transaction, "vrchat.savedAt", DateTime.Now);
        transaction.Commit();
    }

    public Dictionary<string, List<UnityPackageInstallRecord>> LoadInstallLedger()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_key, payload FROM install_records ORDER BY installed_at";
        using var reader = command.ExecuteReader();

        var ledger = new Dictionary<string, List<UnityPackageInstallRecord>>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var record = Deserialize<UnityPackageInstallRecord>(reader.GetString(1));
            if (record is null) continue;

            var key = reader.GetString(0);
            if (!ledger.TryGetValue(key, out var list)) ledger[key] = list = [];
            list.Add(record);
        }

        return ledger;
    }

    public void SaveInstallRecords(string projectKey, IReadOnlyList<UnityPackageInstallRecord> records)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM install_records WHERE project_key = $key";
            delete.Parameters.AddWithValue("$key", projectKey);
            delete.ExecuteNonQuery();
        }

        foreach (var record in records)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO install_records(project_key, package_id, installed_at, payload)
                VALUES($key, $package, $installed, $payload)
                ON CONFLICT(project_key, package_id) DO UPDATE SET
                    installed_at = excluded.installed_at,
                    payload = excluded.payload
                """;
            insert.Parameters.AddWithValue("$key", projectKey);
            insert.Parameters.AddWithValue("$package", record.PackageId);
            insert.Parameters.AddWithValue("$installed", ToUnix(record.InstalledAt));
            insert.Parameters.AddWithValue("$payload", Serialize(record));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ReplaceInstallLedger(IReadOnlyDictionary<string, List<UnityPackageInstallRecord>> ledger)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM install_records";
            delete.ExecuteNonQuery();
        }

        foreach (var (projectKey, records) in ledger)
        {
            foreach (var record in records)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO install_records(project_key, package_id, installed_at, payload) VALUES($key, $package, $installed, $payload)";
                insert.Parameters.AddWithValue("$key", projectKey);
                insert.Parameters.AddWithValue("$package", record.PackageId);
                insert.Parameters.AddWithValue("$installed", ToUnix(record.InstalledAt));
                insert.Parameters.AddWithValue("$payload", Serialize(record));
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static string AssetStoreKey(AssetStoreAsset item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProductId)) return item.ProductId!;

        var normalized = new string(item.Name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Length > 0 ? normalized : item.Name;
    }

    private DateTime GetCacheDate(string key)
    {
        var raw = GetCacheValue(key);
        return long.TryParse(raw, out var unix) ? FromUnix(unix) : default;
    }

    private void SetCacheDate(SqliteConnection connection, SqliteTransaction transaction, string key, DateTime value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cache_meta(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", ToUnix(value).ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private string? GetCacheValue(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM cache_meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void ExportSnapshots()
    {
        var cacheDir = Path.Combine(DataDirectory, "cache");
        Directory.CreateDirectory(cacheDir);

        WriteSnapshot(Path.Combine(cacheDir, "booth-library.json"), LoadBoothCache());
        WriteSnapshot(Path.Combine(cacheDir, "assetstore-account.json"), LoadAssetStoreAccount());
        WriteSnapshot(Path.Combine(cacheDir, "assetstore-covers.json"), LoadAssetStoreCovers());
        WriteSnapshot(Path.Combine(cacheDir, "vrchat-content.json"), LoadVrchatContent());

        var meta = LoadResourceMeta();
        var metaSavedAt = GetCacheDate("resource-meta.savedAt");
        WriteSnapshot(Path.Combine(cacheDir, "resource-meta.json"),
            new ResourceMetaCache { SavedAt = metaSavedAt == default ? DateTime.Now : metaSavedAt, Entries = meta });

        WriteSnapshot(Path.Combine(DataDirectory, "unitypackage-ledger.json"), LoadInstallLedger());
    }

    public void ImportSnapshots()
    {
        var cacheDir = Path.Combine(DataDirectory, "cache");

        var booth = ReadSnapshot<BoothLibraryCache>(Path.Combine(cacheDir, "booth-library.json"));
        if (booth is not null)
        {
            SaveBoothCache(booth.Items);
            if (booth.SavedAt != default) SetCacheDateNow("booth.savedAt", booth.SavedAt);
        }

        var account = ReadSnapshot<AssetStoreAccountCache>(Path.Combine(cacheDir, "assetstore-account.json"));
        if (account is not null)
        {
            SaveAssetStoreAccount(account.Items);
            if (account.SavedAt != default) SetCacheDateNow("assetstore.savedAt", account.SavedAt);
        }

        var covers = ReadSnapshot<Dictionary<string, string>>(Path.Combine(cacheDir, "assetstore-covers.json"));
        if (covers is not null) ReplaceAssetStoreCovers(covers);

        var vrchat = ReadSnapshot<VRChatContentCache>(Path.Combine(cacheDir, "vrchat-content.json"));
        if (vrchat is not null)
        {
            SaveVrchatContent(vrchat.Items);
            if (vrchat.SavedAt != default) SetCacheDateNow("vrchat.savedAt", vrchat.SavedAt);
        }

        var meta = ReadSnapshot<ResourceMetaCache>(Path.Combine(cacheDir, "resource-meta.json"));
        if (meta is not null) ReplaceResourceMeta(meta.Entries);

        var ledger = ReadSnapshot<Dictionary<string, List<UnityPackageInstallRecord>>>(
            Path.Combine(DataDirectory, "unitypackage-ledger.json"));
        if (ledger is not null) ReplaceInstallLedger(ledger);
    }

    private void SetCacheDateNow(string key, DateTime value)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        SetCacheDate(connection, transaction, key, value);
        transaction.Commit();
    }

    private static void WriteSnapshot<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, SnapshotOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    private static T? ReadSnapshot<T>(string path)
    {
        if (!File.Exists(path)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SnapshotOptions);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }
}
