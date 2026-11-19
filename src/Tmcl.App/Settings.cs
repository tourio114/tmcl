using System.IO;
using System.Text.Json;
namespace Tmcl.App;
public sealed class Settings
{
    public static string DataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TMCL");
    public string GamePath { get; set; } = Path.Combine(DataPath, ".minecraft");
    public string OfflineName { get; set; } = "Player";
    public bool MicrosoftMode { get; set; }
    public string ClientId { get; set; } = "";
    public string JavaPath { get; set; } = "";
    public string Version { get; set; } = "";
    public int Threads { get; set; } = 8;
    public int Memory { get; set; } = (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576 / 2, 1024, 4096);
    public bool Dark { get; set; } = true;
    public bool Mirror { get; set; } = true;
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DataPath, "settings.json"))) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataPath);
        var path = Path.Combine(DataPath, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}
