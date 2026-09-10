using System.Text.Json.Serialization;

namespace MatHax.Reborn.Launcher.Models;

public sealed record ModrinthVersion(
    [property: JsonPropertyName("version_number")] string VersionNumber,
    [property: JsonPropertyName("version_type")] string VersionType,
    [property: JsonPropertyName("date_published")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("files")] IReadOnlyList<ModrinthFile> Files);

public sealed record ModrinthFile(
    [property: JsonPropertyName("url")] string DownloadUrl,
    [property: JsonPropertyName("filename")] string FileName,
    [property: JsonPropertyName("primary")] bool IsPrimary,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("hashes")] ModrinthHashes Hashes);

public sealed record ModrinthHashes(
    [property: JsonPropertyName("sha512")] string Sha512);

public sealed record ProtocolCompatibilityMod(string Version, string FilePath);

public sealed record CuratedMod(
    string Slug,
    string Name,
    string Description,
    string? RequiresSlug = null);
