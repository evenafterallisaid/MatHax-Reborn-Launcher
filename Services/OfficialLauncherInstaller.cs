using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http;
using CmlLib.Core;
using CmlLib.Core.ModLoaders.FabricMC;

namespace MatHax.Reborn.Launcher.Services;

public sealed class OfficialLauncherInstaller
{
    private const string ProfileKey = "mathax-reborn";
    private readonly FabricInstaller fabricInstaller = new(new HttpClient());

    public async Task<string> InstallAsync(
        string clientJar,
        string? protocolCompatibilityJar,
        CancellationToken cancellationToken = default)
    {
        string minecraftRoot = MinecraftPath.GetOSDefaultPath();
        MinecraftPath minecraftPath = new(minecraftRoot);
        string gameDirectory = Path.Combine(minecraftRoot, "mathax-reborn");

        Directory.CreateDirectory(gameDirectory);
        string modsDirectory = Path.Combine(gameDirectory, "mods");
        MinecraftService.DeployClientJar(clientJar, modsDirectory);
        MinecraftService.DeployProtocolCompatibility(protocolCompatibilityJar, modsDirectory);
        await fabricInstaller.Install(
            MinecraftService.GameVersion,
            MinecraftService.FabricLoaderVersion,
            minecraftPath,
            MinecraftService.FabricVersionName);

        string profilesPath = Path.Combine(minecraftRoot, "launcher_profiles.json");
        JsonObject root = LoadProfiles(profilesPath);
        JsonObject profiles = root["profiles"] as JsonObject ?? new JsonObject();
        root["profiles"] = profiles;

        string now = DateTime.UtcNow.ToString("O");
        profiles[ProfileKey] = new JsonObject
        {
            ["created"] = now,
            ["gameDir"] = gameDirectory,
            ["icon"] = "Furnace",
            ["lastUsed"] = now,
            ["lastVersionId"] = MinecraftService.FabricVersionName,
            ["name"] = "MatHax Reborn",
            ["type"] = "custom"
        };

        root["version"] ??= 3;
        SaveProfilesAtomically(profilesPath, root);
        return gameDirectory;
    }

    private static JsonObject LoadProfiles(string profilesPath)
    {
        if (!File.Exists(profilesPath)) return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(profilesPath)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The official launcher profile file is not valid JSON.", exception);
        }
    }

    private static void SaveProfilesAtomically(string profilesPath, JsonObject profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profilesPath)!);
        string temporary = profilesPath + ".mathax.tmp";
        string backup = profilesPath + ".mathax.bak";

        if (File.Exists(profilesPath)) File.Copy(profilesPath, backup, true);
        File.WriteAllText(temporary, profiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, profilesPath, true);
    }
}
