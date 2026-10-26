using System.Text.Json;
using Akashic.Core.Models;
using Microsoft.Data.Sqlite;

namespace Akashic.Core.Data;

public sealed partial class AkashicDatabase
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AkashicDatabase(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "akashic.db");

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();

        EnsureSchemaVersion(connection);
        ImportLegacyOnce();
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();

        return connection;
    }

    public string? GetMeta(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO meta(key, value) VALUES($key, $value) " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    public long Count(string table)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static long ToUnix(DateTime value) =>
        value == default ? 0 : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    private static DateTime FromUnix(long value) =>
        value <= 0 ? default : DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;

    private void EnsureSchemaVersion(SqliteConnection connection)
    {
        var current = GetMeta("schema.version");
        if (current is null)
        {
            SetMeta("schema.version", SchemaVersion.ToString());
            return;
        }

        if (int.TryParse(current, out var version) && version > SchemaVersion)
            throw new InvalidOperationException($"数据库版本 {version} 高于程序支持的 {SchemaVersion}。");
    }

    private void ImportLegacyOnce()
    {
        if (GetMeta("legacy.imported") == "1") return;

        ImportSnapshots();
        SetMeta("legacy.imported", "1");
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS projects (
            path TEXT PRIMARY KEY COLLATE NOCASE,
            name TEXT NOT NULL,
            kind TEXT NOT NULL,
            unity_version TEXT,
            last_modified INTEGER NOT NULL,
            package_count INTEGER NOT NULL,
            signature TEXT NOT NULL,
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_projects_kind ON projects(kind);

        CREATE TABLE IF NOT EXISTS installed_packages (
            project_path TEXT NOT NULL COLLATE NOCASE,
            name TEXT NOT NULL COLLATE NOCASE,
            version TEXT,
            display_name TEXT,
            source TEXT,
            PRIMARY KEY(project_path, name)
        );
        CREATE INDEX IF NOT EXISTS idx_installed_name ON installed_packages(name);

        CREATE TABLE IF NOT EXISTS vpm_packages (
            source TEXT NOT NULL,
            source_name TEXT,
            name TEXT NOT NULL COLLATE NOCASE,
            display_name TEXT,
            latest TEXT,
            payload TEXT NOT NULL,
            PRIMARY KEY(source, name)
        );
        CREATE INDEX IF NOT EXISTS idx_vpm_name ON vpm_packages(name);

        CREATE TABLE IF NOT EXISTS vpm_sources (
            source TEXT PRIMARY KEY,
            fetched_at INTEGER NOT NULL,
            package_count INTEGER NOT NULL,
            error TEXT
        );

        CREATE TABLE IF NOT EXISTS resource_meta (
            meta_key TEXT PRIMARY KEY COLLATE NOCASE,
            category TEXT,
            payload TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS resources (
            key TEXT PRIMARY KEY,
            meta_key TEXT COLLATE NOCASE,
            name TEXT COLLATE NOCASE,
            origin TEXT,
            category TEXT,
            last_modified INTEGER NOT NULL,
            matched INTEGER NOT NULL,
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_resources_origin ON resources(origin);
        CREATE INDEX IF NOT EXISTS idx_resources_category ON resources(category);
        CREATE INDEX IF NOT EXISTS idx_resources_name ON resources(name);

        CREATE TABLE IF NOT EXISTS package_files (
            path TEXT PRIMARY KEY COLLATE NOCASE,
            size INTEGER NOT NULL,
            modified INTEGER NOT NULL,
            payload TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS archive_files (
            path TEXT PRIMARY KEY COLLATE NOCASE,
            size INTEGER NOT NULL,
            modified INTEGER NOT NULL,
            payload TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS booth_items (
            number TEXT PRIMARY KEY,
            title TEXT,
            shop TEXT,
            category TEXT,
            is_free INTEGER NOT NULL,
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_booth_title ON booth_items(title);
        CREATE INDEX IF NOT EXISTS idx_booth_category ON booth_items(category);

        CREATE TABLE IF NOT EXISTS assetstore_items (
            product_key TEXT PRIMARY KEY COLLATE NOCASE,
            name TEXT COLLATE NOCASE,
            publisher TEXT,
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_assetstore_name ON assetstore_items(name);

        CREATE TABLE IF NOT EXISTS assetstore_covers (
            match_key TEXT PRIMARY KEY COLLATE NOCASE,
            url TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS vrchat_items (
            id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            name TEXT,
            payload TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_vrchat_kind ON vrchat_items(kind);

        CREATE TABLE IF NOT EXISTS install_records (
            project_key TEXT NOT NULL COLLATE NOCASE,
            package_id TEXT NOT NULL,
            installed_at INTEGER NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY(project_key, package_id)
        );

        CREATE TABLE IF NOT EXISTS cache_meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS thumbnail_cache (
            path TEXT PRIMARY KEY COLLATE NOCASE,
            size INTEGER NOT NULL,
            modified INTEGER NOT NULL,
            thumbnail_path TEXT
        );

        CREATE TABLE IF NOT EXISTS preview_materials (
            cache_key TEXT PRIMARY KEY,
            size INTEGER NOT NULL,
            modified INTEGER NOT NULL,
            payload TEXT NOT NULL
        );
        """;
}
