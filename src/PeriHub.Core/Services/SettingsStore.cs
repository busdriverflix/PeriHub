using System.Text.Json;

namespace PeriHub.Core.Services;

public sealed record HeadsetSettings(double[]? Eq = null, int? Sidetone = null, int? InactiveMinutes = null);

/// <summary>Persists what the user set (only values they actually set) in ~/.config/perihub/settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "perihub", "settings.json");

    private readonly object _lock = new();
    private HeadsetSettings _headset = Load();
    public HeadsetSettings Headset { get { lock (_lock) return _headset; } }

    public void UpdateHeadset(Func<HeadsetSettings, HeadsetSettings> change)
    {
        lock (_lock)
        {
            _headset = change(_headset);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp"; // write + rename so a crash never leaves a half-written file
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { headset = _headset }, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    private static HeadsetSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            return doc.RootElement.TryGetProperty("headset", out var h) ? h.Deserialize<HeadsetSettings>(Json) ?? new() : new();
        }
        catch (Exception ex) { Console.Error.WriteLine($"Could not read settings: {ex.Message}"); return new(); }
    }
}
