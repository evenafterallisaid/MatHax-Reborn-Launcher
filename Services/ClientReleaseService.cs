using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Security.Cryptography;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;

namespace MatHax.Reborn.Launcher.Services;

public sealed class ClientReleaseService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/evenafterallisaid/MatHax-Reborn/releases/latest";
    private readonly HttpClient httpClient;

    public ClientReleaseService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MatHax-Reborn-Launcher", "0.2.1"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ClientRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        GitHubRelease release = await httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");

        GitHubAsset asset = release.Assets.FirstOrDefault(item =>
            item.Name.StartsWith("mathax-reborn-", StringComparison.OrdinalIgnoreCase) &&
            item.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
            !item.Name.Contains("sources", StringComparison.OrdinalIgnoreCase) &&
            !item.Name.Contains("javadoc", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The latest MatHax Reborn release has no client JAR.");

        if (asset.Digest is null || !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The latest MatHax release has no GitHub SHA-256 digest.");

        return new ClientRelease(
            release.TagName,
            release.HtmlUrl,
            asset.Name,
            asset.DownloadUrl,
            asset.Size,
            asset.Digest["sha256:".Length..]);
    }

    public async Task<string> EnsureDownloadedAsync(
        ClientRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        string destination = Path.Combine(AppPaths.Cache, release.JarName);
        if (File.Exists(destination) &&
            new FileInfo(destination).Length == release.Size &&
            await HasExpectedHashAsync(destination, release.Sha256, cancellationToken))
            return destination;

        string temporary = destination + ".download";
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? release.Size;
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

            if (!await HasExpectedHashAsync(temporary, release.Sha256, cancellationToken))
                throw new InvalidDataException("MatHax Reborn failed its GitHub SHA-256 integrity check.");

            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? FindCachedJar() => Directory.Exists(AppPaths.Cache)
        ? Directory.GetFiles(AppPaths.Cache, "mathax-reborn-*.jar").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
