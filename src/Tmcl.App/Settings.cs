using System.IO;
using System.Text.Json;
namespace Tmcl.App;
public sealed class Settings
{
    public static string DataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "TMCL");
    public string GamePath { get; set; } = Path.Combine(DataPath, ".minecraft");
    public string OfflineName { get; set; } = "Player";
    public bool MicrosoftMode { get; set; }
    public string ClientId { get; set; } = "";
    public string JavaPath { get; set; } = "";
    public string Version { get; set; } = "1.8.9";
    public int Threads { get; set; } = 8;
    public int Memory { get; set; } = (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576 / 2, 1024, 4096);
    public bool Dark { get; set; } = false;
    public string? Theme { get; set; }
    public string InterfaceStyle { get; set; } = "PowerToys";
    public string SourceMode { get; set; } = "Auto";
    public bool? AutoMirror { get; set; }
    public string CountryCode { get; set; } = "";
    public string IPv4 { get; set; } = "";
    public DateTimeOffset? NetworkDetectedAt { get; set; }
    public bool UseMirror => SourceMode switch { "BMCLAPI" => true, "Official" => false, _ => AutoMirror ?? Tmcl.Core.DownloadSources.DefaultMirror(CountryCode) };
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
