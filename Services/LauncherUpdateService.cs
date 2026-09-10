using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;

namespace MatHax.Reborn.Launcher.Services;

public sealed class LauncherUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/evenafterallisaid/MatHax-Reborn-Launcher/releases/latest";

    private readonly HttpClient httpClient;

    public LauncherUpdateService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MatHax-Reborn-Launcher", "0.2.1"));
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<LauncherUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        GitHubRelease release = await httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty launcher release response.");

        string versionText = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out Version? available)) return null;

        Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (available <= current) return null;

        GitHubAsset asset = release.Assets.FirstOrDefault(item =>
            item.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The latest launcher release has no Windows x64 package.");

        if (asset.Digest is null || !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The launcher update has no GitHub SHA-256 digest.");

        return new LauncherUpdate(
            release.TagName,
            asset.DownloadUrl,
            asset.Size,
            asset.Digest["sha256:".Length..]);
    }

    public async Task StageAndRestartAsync(
        LauncherUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        string package = Path.Combine(AppPaths.Updates, $"launcher-{update.Tag}.zip");
        string temporary = package + ".download";

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? update.Size;
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

            await using (FileStream stream = File.OpenRead(temporary))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The launcher update failed its GitHub SHA-256 integrity check.");
            }

            File.Move(temporary, package, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        string stage = Path.Combine(AppPaths.Updates, "staged");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        ZipFile.ExtractToDirectory(package, stage);

        string stagedExecutable = Directory.GetFiles(
                stage,
                "MatHax Reborn Launcher.exe",
                SearchOption.AllDirectories)
            .SingleOrDefault()
            ?? throw new InvalidDataException("The launcher update package does not contain the launcher executable.");

        string currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current launcher executable path is unavailable.");
        if (!Path.GetFileName(currentExecutable).Equals(
                "MatHax Reborn Launcher.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Self-update is available only from the packaged launcher executable.");

        string script = Path.Combine(AppPaths.Updates, "apply-update.ps1");
        static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = 'Stop'
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 400
            Copy-Item -LiteralPath '{{Quote(stagedExecutable)}}' -Destination '{{Quote(currentExecutable)}}' -Force
            Start-Process -FilePath '{{Quote(currentExecutable)}}'
            Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force
            """);

        ProcessStartInfo updater = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        updater.ArgumentList.Add("-NoProfile");
        updater.ArgumentList.Add("-ExecutionPolicy");
        updater.ArgumentList.Add("Bypass");
        updater.ArgumentList.Add("-File");
        updater.ArgumentList.Add(script);
        Process.Start(updater);
    }
}
