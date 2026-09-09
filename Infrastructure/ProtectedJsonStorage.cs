using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using XboxAuthNet.Game.Accounts.JsonStorage;

namespace MatHax.Reborn.Launcher.Infrastructure;

public sealed class ProtectedJsonStorage(string filePath) : IJsonStorage
{
    public JsonNode? ReadAsJsonNode()
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            byte[] encrypted = File.ReadAllBytes(filePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonNode.Parse(decrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(JsonNode node, JsonSerializerOptions? serializerOptions)
    {
        using MemoryStream stream = new();
        JsonSerializer.Serialize(stream, node, serializerOptions);
        byte[] encrypted = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.CurrentUser);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        string temporary = filePath + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, filePath, true);
    }
}
