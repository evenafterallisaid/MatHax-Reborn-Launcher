using System.Text.Json;

namespace MatHax.Reborn.Launcher.Models;

public sealed class LauncherSettings
{
    public int MemoryGb { get; set; } = 4;

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path)) ?? new LauncherSettings();
        }
        catch (JsonException)
        {
        }

        return new LauncherSettings();
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
