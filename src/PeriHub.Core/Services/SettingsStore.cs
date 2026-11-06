using System.Text.Json;

namespace PeriHub.Core.Services;

public sealed record HeadsetSettings(double[]? Eq = null, int? Sidetone = null, int? InactiveMinutes = null);
public sealed record SurroundSettings(bool Enabled = false, bool SwapSides = false);
public sealed record KeyboardSettings(string? Mode = null, string? Color = null);
public sealed record MouseSettings(string? Mode = null, string? Top = null, string? Middle = null, string? Bottom = null);

/// <summary>Persists what the user set (only values they actually set) in ~/.config/perihub/settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "perihub", "settings.json");

    private readonly object _lock = new();
    private HeadsetSettings _headset;
    private SurroundSettings _surround;
    private MouseSettings _mouse;
    private KeyboardSettings _keyboard;
    public HeadsetSettings Headset { get { lock (_lock) return _headset; } }
    public SurroundSettings Surround { get { lock (_lock) return _surround; } }
    public MouseSettings Mouse { get { lock (_lock) return _mouse; } }
    public KeyboardSettings Keyboard { get { lock (_lock) return _keyboard; } }

    public SettingsStore() => (_headset, _surround, _mouse, _keyboard) = Load();

    public void UpdateHeadset(Func<HeadsetSettings, HeadsetSettings> change) { lock (_lock) { _headset = change(_headset); Save(); } }
    public void UpdateKeyboard(Func<KeyboardSettings, KeyboardSettings> change) { lock (_lock) { _keyboard = change(_keyboard); Save(); } }
    public void UpdateMouse(Func<MouseSettings, MouseSettings> change) { lock (_lock) { _mouse = change(_mouse); Save(); } }
    public void UpdateSurround(Func<SurroundSettings, SurroundSettings> change) { lock (_lock) { _surround = change(_surround); Save(); } }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp"; // write + rename so a crash never leaves a half-written file
        File.WriteAllText(tmp, JsonSerializer.Serialize(new { headset = _headset, surround = _surround, mouse = _mouse, keyboard = _keyboard }, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static (HeadsetSettings, SurroundSettings, MouseSettings, KeyboardSettings) Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return (new(), new(), new(), new());
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            return (root.TryGetProperty("headset", out var h) ? h.Deserialize<HeadsetSettings>(Json) ?? new() : new(),
                    root.TryGetProperty("surround", out var s) ? s.Deserialize<SurroundSettings>(Json) ?? new() : new(),
                    root.TryGetProperty("mouse", out var m) ? m.Deserialize<MouseSettings>(Json) ?? new() : new(),
                    root.TryGetProperty("keyboard", out var k) ? k.Deserialize<KeyboardSettings>(Json) ?? new() : new());
        }
        catch (Exception ex) { Console.Error.WriteLine($"Could not read settings: {ex.Message}"); return (new(), new(), new(), new()); }
    }
}
