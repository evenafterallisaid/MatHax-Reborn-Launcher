using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;

namespace MatHax.Reborn.Launcher.Services;

public sealed class CuratedModService
{
    private readonly HttpClient httpClient;
    private readonly Dictionary<string, ManagedMod> installed;

    public IReadOnlyList<CuratedMod> Catalog { get; } =
    [
        new("sodium", "Sodium", "Rendering optimization with large frame-rate and stability improvements."),
        new("lithium", "Lithium", "Game-logic optimization that improves tick performance without changing behavior."),
        new("iris", "Iris", "Shader-pack support designed around Sodium.", "sodium")
    ];

    public event Action<string, double?>? ProgressChanged;

    public CuratedModService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MatHax-Reborn-Launcher", "0.2.1"));
        installed = LoadManifest();
    }

    public bool IsInstalled(string slug)
    {
        if (!installed.TryGetValue(slug, out ManagedMod? mod)) return false;
        return File.Exists(Path.Combine(AppPaths.Instance, "mods", Path.GetFileName(mod.FileName)));
    }

    public string? InstalledVersion(string slug) =>
        installed.TryGetValue(slug, out ManagedMod? mod) && IsInstalled(slug) ? mod.Version : null;

    public async Task InstallAsync(CuratedMod mod, CancellationToken cancellationToken = default)
    {
        if (mod.RequiresSlug is { } dependencySlug)
        {
            CuratedMod dependency = Catalog.Single(item => item.Slug == dependencySlug);
            await InstallAsync(dependency, cancellationToken);
        }

        Report($"Finding a Minecraft {MinecraftService.GameVersion} build of {mod.Name}…", null);
        string versionsApi =
            $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(mod.Slug)}/version" +
            "?loaders=%5B%22fabric%22%5D&game_versions=%5B%2226.2%22%5D&include_changelog=false";

        IReadOnlyList<ModrinthVersion> versions =
            await httpClient.GetFromJsonAsync<IReadOnlyList<ModrinthVersion>>(versionsApi, cancellationToken)
            ?? throw new InvalidOperationException($"Modrinth returned an empty {mod.Name} response.");
        ModrinthVersion version = versions
            .Where(item => item.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.PublishedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No stable {mod.Name} build supports Minecraft 26.2.");
        ModrinthFile file = version.Files.FirstOrDefault(item => item.IsPrimary)
            ?? version.Files.FirstOrDefault()
            ?? throw new InvalidOperationException($"The {mod.Name} release has no downloadable JAR.");

        string safeName = Path.GetFileName(file.FileName);
        if (!safeName.Equals(file.FileName, StringComparison.Ordinal) ||
            !safeName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Modrinth returned an unsafe filename for {mod.Name}.");

        string cacheDirectory = Path.Combine(AppPaths.Cache, "mods");
        Directory.CreateDirectory(cacheDirectory);
        string cached = Path.Combine(cacheDirectory, safeName);
        if (!File.Exists(cached) || !await HasExpectedHashAsync(cached, file.Hashes.Sha512, cancellationToken))
            await DownloadAsync(mod.Name, file, cached, cancellationToken);

        string modsDirectory = Path.Combine(AppPaths.Instance, "mods");
        Directory.CreateDirectory(modsDirectory);
        string destination = Path.Combine(modsDirectory, safeName);
        File.Copy(cached, destination, true);

        if (installed.TryGetValue(mod.Slug, out ManagedMod? previous) &&
            !previous.FileName.Equals(safeName, StringComparison.OrdinalIgnoreCase))
            DeleteManagedFile(previous.FileName, modsDirectory);

        installed[mod.Slug] = new ManagedMod(safeName, version.VersionNumber);
        SaveManifest();
        Report($"{mod.Name} {version.VersionNumber} installed.", 100);
    }

    public void Uninstall(CuratedMod mod)
    {
        if (mod.Slug == "sodium" && IsInstalled("iris"))
            throw new InvalidOperationException("Remove Iris before removing Sodium because Iris depends on it.");

        if (installed.Remove(mod.Slug, out ManagedMod? entry))
            DeleteManagedFile(entry.FileName, Path.Combine(AppPaths.Instance, "mods"));
        SaveManifest();
        Report($"{mod.Name} removed.", 0);
    }

    private async Task DownloadAsync(
        string name,
        ModrinthFile file,
        string destination,
        CancellationToken cancellationToken)
    {
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
                    if (total > 0) Report($"Downloading {name}…", read * 100d / total);
                }

                await output.FlushAsync(cancellationToken);
            }

            if (!await HasExpectedHashAsync(temporary, file.Hashes.Sha512, cancellationToken))
                throw new InvalidDataException($"{name} failed its SHA-512 integrity check.");
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA512.HashDataAsync(stream, cancellationToken));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteManagedFile(string fileName, string modsDirectory)
    {
        string root = Path.GetFullPath(modsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(modsDirectory, Path.GetFileName(fileName)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A managed mod path escaped the MatHax mods directory.");
        if (File.Exists(path)) File.Delete(path);
    }

    private static Dictionary<string, ManagedMod> LoadManifest()
    {
        try
        {
            if (File.Exists(AppPaths.ManagedMods))
            {
                Dictionary<string, ManagedMod>? values =
                    JsonSerializer.Deserialize<Dictionary<string, ManagedMod>>(
                        File.ReadAllText(AppPaths.ManagedMods));
                if (values is not null)
                    return new Dictionary<string, ManagedMod>(values, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
        }

        return new Dictionary<string, ManagedMod>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveManifest()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(
            AppPaths.ManagedMods,
            JsonSerializer.Serialize(installed, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Report(string message, double? progress) => ProgressChanged?.Invoke(message, progress);
}
