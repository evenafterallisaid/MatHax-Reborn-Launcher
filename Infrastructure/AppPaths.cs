namespace MatHax.Reborn.Launcher.Infrastructure;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MatHax Reborn Launcher");

    public static readonly string Instance = Path.Combine(Root, "instance");
    public static readonly string Cache = Path.Combine(Root, "cache");
    public static readonly string Settings = Path.Combine(Root, "settings.json");
    public static readonly string Accounts = Path.Combine(Root, "accounts.bin");
    public static readonly string ManagedMods = Path.Combine(Root, "managed-mods.json");
    public static readonly string Updates = Path.Combine(Root, "updates");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Instance);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Updates);
    }
}
