using System.IO;
using System.Text.Json;

namespace Rain;

public enum RepeatMode { Off, All, One }

public sealed class Settings
{
    public string? Folder { get; set; }
    public double Volume { get; set; } = 0.8;
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rain", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }
}
