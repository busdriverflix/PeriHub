using System.Text.RegularExpressions;
using PeriHub.Core.Services;

namespace PeriHub.Core.Devices;

/// <summary>SteelSeries Aerox 5 Wireless via the rivalcfg CLI (battery, 3-zone lighting).</summary>
public sealed partial class MouseDevice : IDevice
{
    public string Id => "mouse";
    private readonly SemaphoreSlim _lock = new(1, 1); // one HID conversation at a time

    private static string Exe => Environment.GetEnvironmentVariable("PERIHUB_RIVALCFG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "perihub", "venv", "bin", "rivalcfg");

    public async Task<DeviceStatus> QueryAsync(CancellationToken ct)
    {
        const string name = "SteelSeries Aerox 5";
        var info = new Dictionary<string, string>();
        if (!File.Exists(Exe)) return new(Id, name, false, null, "rivalcfg nicht installiert", info);
        try
        {
            var (_, output) = await RunLocked(["--battery-level"], ct);
            var m = BatteryRegex().Match(output);
            if (!m.Success) return new(Id, name, false, null, "Maus schläft oder ist nicht erreichbar", info);
            var state = m.Groups[1].Value switch { "Charging" => "Lädt", "Discharging" => "Verbunden", var s => s };
            return new(Id, name, true, int.Parse(m.Groups[2].Value), state, info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(Id, name, false, null, $"Fehler: {ex.Message}", info);
        }
    }

    /// <summary>mode: "static" (three zone colors), "rainbow" or "off". Colors are RRGGBB hex, with or without '#'.</summary>
    public async Task SetLightsAsync(string mode, string? top, string? middle, string? bottom, CancellationToken ct)
    {
        string[] args = mode switch
        {
            "rainbow" => ["--rainbow-effect"],
            "off" => ["--top-color", "000000", "--middle-color", "000000", "--bottom-color", "000000"],
            "static" => ["--top-color", Hex(top), "--middle-color", Hex(middle), "--bottom-color", Hex(bottom)],
            _ => throw new ArgumentException($"Unbekannter Licht-Modus '{mode}'"),
        };
        var (code, output) = await RunLocked(args, ct);
        if (code != 0) throw new InvalidOperationException($"rivalcfg: {output.Trim()}");
    }

    /// <summary>Only plain 6-digit hex gets through, so a value can never be mistaken for a command-line option.</summary>
    private static string Hex(string? c)
    {
        var h = (c ?? "").TrimStart('#');
        if (!HexRegex().IsMatch(h)) throw new ArgumentException($"Ungültige Farbe '{c}'");
        return h;
    }

    private async Task<(int, string)> RunLocked(string[] args, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return await ProcessRunner.RunAsync(Exe, args, TimeSpan.FromSeconds(15), ct); }
        finally { _lock.Release(); }
    }

    [GeneratedRegex(@"(\w+)\s*\[[= ]*\]\s*(\d+)\s*%")] private static partial Regex BatteryRegex();
    [GeneratedRegex("^[0-9a-fA-F]{6}$")] private static partial Regex HexRegex();
}
