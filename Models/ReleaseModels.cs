using System.Text.Json.Serialization;

namespace MatHax.Reborn.Launcher.Models;

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);

public sealed record ClientRelease(
    string Tag,
    string PageUrl,
    string JarName,
    string DownloadUrl,
    long Size,
    string Sha256);

public sealed record LauncherUpdate(string Tag, string DownloadUrl, long Size, string Sha256);
