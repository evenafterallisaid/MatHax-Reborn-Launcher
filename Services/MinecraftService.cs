using System.Diagnostics;
using System.Net.Http;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using MatHax.Reborn.Launcher.Infrastructure;
using MatHax.Reborn.Launcher.Models;
using XboxAuthNet.Game.Accounts;

namespace MatHax.Reborn.Launcher.Services;

public sealed class MinecraftService
{
    public const string GameVersion = "26.2";
    public const string FabricLoaderVersion = "0.19.3";
    public const string FabricVersionName = "mathax-reborn-26.2";

    private readonly ClientReleaseService releases;
    private readonly MinecraftPath instancePath;
    private readonly MinecraftLauncher launcher;
    private readonly FabricInstaller fabricInstaller;
    private readonly ProtocolCompatibilityService protocolCompatibility = new();
    private readonly JELoginHandler loginHandler;

    public MSession? Session { get; private set; }
    public event Action<string, double?>? ProgressChanged;

    public MinecraftService(ClientReleaseService releases)
    {
        AppPaths.EnsureCreated();
        this.releases = releases;
        instancePath = new MinecraftPath(AppPaths.Instance);
        launcher = new MinecraftLauncher(instancePath);
        fabricInstaller = new FabricInstaller(new HttpClient());

        JsonXboxGameAccountManager accountManager = new(
            new ProtectedJsonStorage(AppPaths.Accounts),
            JEGameAccount.FromSessionStorage,
            JsonXboxGameAccountManager.DefaultSerializerOption);
        loginHandler = new JELoginHandlerBuilder().WithAccountManager(accountManager).Build();

        launcher.FileProgressChanged += (_, progress) =>
        {
            double percent = progress.TotalTasks > 0 ? progress.ProgressedTasks * 100d / progress.TotalTasks : 0;
            Report(progress.Name is null ? "Preparing game files…" : $"Preparing {progress.Name}", percent);
        };
        launcher.ByteProgressChanged += (_, progress) =>
        {
            if (progress.TotalBytes > 0)
                Report("Downloading Minecraft files…", progress.ProgressedBytes * 100d / progress.TotalBytes);
        };
    }

    public string? StoredAccountName => loginHandler.AccountManager.GetAccounts()
        .OfType<JEGameAccount>()
        .Select(account => account.Profile?.Username)
        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

    public async Task<MSession?> TrySilentLoginAsync(CancellationToken cancellationToken = default)
    {
        if (loginHandler.AccountManager.GetAccounts().Count == 0) return null;

        try
        {
            Report("Refreshing Microsoft session…", null);
            Session = await loginHandler.AuthenticateSilently(cancellationToken);
            Report($"Signed in as {Session.Username}", 0);
            return Session;
        }
        catch
        {
            Session = null;
            Report("Microsoft session needs to be renewed.", 0);
            return null;
        }
    }

    public async Task<MSession> SignInAsync(CancellationToken cancellationToken = default)
    {
        Report("Opening secure Microsoft sign-in…", null);
        Session = await loginHandler.AuthenticateInteractively(cancellationToken);
        Report($"Signed in as {Session.Username}", 0);
        return Session;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (loginHandler.AccountManager.GetAccounts().Count > 0)
            await loginHandler.Signout(cancellationToken);
        loginHandler.AccountManager.ClearAccounts();
        loginHandler.AccountManager.SaveAccounts();
        Session = null;
        Report("Signed out.", 0);
    }

    public async Task<ClientRelease?> CheckLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await releases.GetLatestAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<Process> LaunchAsync(
        int memoryGb,
        bool wideServerSupport,
        CancellationToken cancellationToken = default)
    {
        if (Session is null) await SignInAsync(cancellationToken);

        string clientJar = await ResolveClientJarAsync(cancellationToken);
        string modsDirectory = Path.Combine(AppPaths.Instance, "mods");
        DeployClientJar(clientJar, modsDirectory);

        ProtocolCompatibilityMod? protocolMod = wideServerSupport
            ? await ResolveProtocolCompatibilityAsync(cancellationToken)
            : null;
        DeployProtocolCompatibility(protocolMod?.FilePath, modsDirectory);

        Report("Installing Fabric profile…", null);
        await fabricInstaller.Install(GameVersion, FabricLoaderVersion, instancePath, FabricVersionName);

        Report("Resolving Minecraft, libraries, assets, and Java…", null);
        Process process = await launcher.InstallAndBuildProcessAsync(
            FabricVersionName,
            new MLaunchOption
            {
                Session = Session,
                MaximumRamMb = Math.Clamp(memoryGb, 2, 32) * 1024,
                GameLauncherName = "MatHax Reborn Launcher",
                GameLauncherVersion = "0.2.1"
            },
            cancellationToken);

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Report("Minecraft closed.", 0);
        process.Start();
        Report("MatHax Reborn is running.", 100);
        return process;
    }

    public async Task RepairAsync(
        bool wideServerSupport,
        CancellationToken cancellationToken = default)
    {
        string modsDirectory = Path.Combine(AppPaths.Instance, "mods");
        string clientJar = await ResolveClientJarAsync(cancellationToken);
        DeployClientJar(clientJar, modsDirectory);

        ProtocolCompatibilityMod? protocolMod = wideServerSupport
            ? await ResolveProtocolCompatibilityAsync(cancellationToken)
            : null;
        DeployProtocolCompatibility(protocolMod?.FilePath, modsDirectory);

        Report("Repairing the Fabric profile…", null);
        await fabricInstaller.Install(GameVersion, FabricLoaderVersion, instancePath, FabricVersionName);
        Report("Checking Minecraft libraries, assets, runtime, and natives…", null);
        await launcher.InstallAsync(FabricVersionName, cancellationToken);
        Report("Repair complete. All required game files passed verification.", 100);
    }

    public async Task<ProtocolCompatibilityMod> ResolveProtocolCompatibilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            Report("Finding the latest compatible ViaFabricPlus build…", null);
            ProtocolCompatibilityMod result = await protocolCompatibility.EnsureLatestAsync(
                new Progress<double>(value => Report("Downloading ViaFabricPlus…", value)),
                cancellationToken);
            Report($"ViaFabricPlus {result.Version} is ready.", 100);
            return result;
        }
        catch when (protocolCompatibility.FindCachedJar() is { } cached)
        {
            Report("Modrinth is unavailable; using cached ViaFabricPlus.", 0);
            return new ProtocolCompatibilityMod("cached", cached);
        }
    }

    public async Task<string> ResolveClientJarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Report("Checking the latest MatHax Reborn release…", null);
            ClientRelease release = await releases.GetLatestAsync(cancellationToken);
            Report($"Downloading {release.Tag}…", 0);
            return await releases.EnsureDownloadedAsync(
                release,
                new Progress<double>(value => Report($"Downloading {release.Tag}…", value)),
                cancellationToken);
        }
        catch when (releases.FindCachedJar() is { } cached)
        {
            Report("GitHub is unavailable; using the cached MatHax client.", 0);
            return cached;
        }
    }

    public static string DeployClientJar(string clientJar, string modsDirectory)
    {
        Directory.CreateDirectory(modsDirectory);
        string destination = Path.Combine(modsDirectory, Path.GetFileName(clientJar));

        foreach (string oldJar in Directory.GetFiles(modsDirectory, "mathax-reborn-*.jar"))
        {
            if (!Path.GetFullPath(oldJar).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Delete(oldJar);
        }

        File.Copy(clientJar, destination, true);
        return destination;
    }

    public static string? DeployProtocolCompatibility(string? protocolJar, string modsDirectory)
    {
        Directory.CreateDirectory(modsDirectory);
        string? destination = protocolJar is null
            ? null
            : Path.Combine(modsDirectory, Path.GetFileName(protocolJar));

        foreach (string oldJar in Directory.GetFiles(modsDirectory, "ViaFabricPlus-*.jar"))
        {
            if (destination is null ||
                !Path.GetFullPath(oldJar).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                File.Delete(oldJar);
        }

        if (protocolJar is null) return null;
        File.Copy(protocolJar, destination!, true);
        return destination;
    }

    private void Report(string message, double? progress) => ProgressChanged?.Invoke(message, progress);
}
