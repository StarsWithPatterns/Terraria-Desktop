using System.Text.Json;

namespace DesktopOverlay;

/// <summary>Remembered between runs in %AppData%\TerrariaDesktop\overlay.json.</summary>
internal sealed class Settings
{
    public OverlayMode Mode { get; set; } = OverlayMode.Wander;
    public int Scale { get; set; } = 2;
    public int Port { get; set; } = FrameProtocol.DefaultPort;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerrariaDesktop", "overlay.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
