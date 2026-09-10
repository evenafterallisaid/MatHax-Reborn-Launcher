using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;

namespace MatHax.Reborn.Launcher.Services;

public sealed class ProtocolCompatibilityService
{
    private const string VersionsApi =
        "https://api.modrinth.com/v2/project/rIC2XJV4/version?loaders=%5B%22fabric%22%5D&game_versions=%5B%2226.2%22%5D&include_changelog=false";

    private readonly HttpClient httpClient;

    public ProtocolCompatibilityService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MatHax-Reborn-Launcher", "0.2.1"));
    }

    public async Task<ProtocolCompatibilityMod> EnsureLatestAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModrinthVersion> versions =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ModrinthVersion>>(VersionsApi, cancellationToken)
            ?? throw new InvalidOperationException("Modrinth returned an empty ViaFabricPlus response.");

        ModrinthVersion version = versions
            .Where(item => item.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PublishedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No stable ViaFabricPlus build supports Minecraft 26.2.");

        ModrinthFile file = version.Files.FirstOrDefault(item => item.IsPrimary)
            ?? version.Files.FirstOrDefault()
            ?? throw new InvalidOperationException("The ViaFabricPlus release has no downloadable JAR.");

        string safeName = Path.GetFileName(file.FileName);
        if (!safeName.Equals(file.FileName, StringComparison.Ordinal) ||
            !safeName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Modrinth returned an unsafe ViaFabricPlus filename.");

        AppPaths.EnsureCreated();
        string destination = Path.Combine(AppPaths.Cache, safeName);
        if (File.Exists(destination) && await HasExpectedHashAsync(destination, file.Hashes.Sha512, cancellationToken))
            return new ProtocolCompatibilityMod(version.VersionNumber, destination);

        string temporary = destination + ".download";
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                file.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? file.Size;
            long read = 0;
            byte[] buffer = new byte[128 * 1024];

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream output = new(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
                true))
            {
                while (true)
                {
                    int count = await input.ReadAsync(buffer, cancellationToken);
                    if (count == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    read += count;
                    if (total > 0) progress?.Report(read * 100d / total);
                }

                await output.FlushAsync(cancellationToken);
            }

            if (!await HasExpectedHashAsync(temporary, file.Hashes.Sha512, cancellationToken))
                throw new InvalidDataException("ViaFabricPlus failed its SHA-512 integrity check.");

            File.Move(temporary, destination, true);
            return new ProtocolCompatibilityMod(version.VersionNumber, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? FindCachedJar() => Directory.Exists(AppPaths.Cache)
        ? Directory.GetFiles(AppPaths.Cache, "ViaFabricPlus-*.jar")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
        : null;

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        byte[] hash = await SHA512.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
