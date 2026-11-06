using System.Text.RegularExpressions;
using PeriHub.Core.Services;

namespace PeriHub.Core.Devices;

/// <summary>
/// ROCCAT Vulcan II lighting via the OpenRGB command line. OpenRGB gets its own config (~/.local/share/perihub/openrgb/config)
/// in which only the Vulcan II detectors are enabled, so it never touches RAM, mainboard or other devices.
/// </summary>
public sealed partial class KeyboardDevice : IDevice
{
    public string Id => "keyboard";
    private const string DeviceName = "Roccat Vulcan II";
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Exe => Environment.GetEnvironmentVariable("PERIHUB_OPENRGB")
        ?? Path.Combine(Home, ".local", "share", "perihub", "openrgb", "OpenRGB.AppImage");
    private static string ConfigDir => Path.Combine(Home, ".local", "share", "perihub", "openrgb", "config");

    /// <summary>Present when the kernel has a HID device with the Vulcan II's USB id (1e7d:2f4e).</summary>
    public Task<DeviceStatus> QueryAsync(CancellationToken ct)
    {
        var present = Directory.Exists("/sys/bus/hid/devices") &&
                      Directory.GetDirectories("/sys/bus/hid/devices", "*:1E7D:2F4E.*").Length > 0;
        var info = new Dictionary<string, string>();
        var state = !present ? "Nicht verbunden" : File.Exists(Exe) ? "Verbunden" : "OpenRGB nicht installiert";
        return Task.FromResult(new DeviceStatus(Id, "ROCCAT Vulcan II", present, null, state, info));
    }

    /// <summary>mode: "static" (one color), "rainbow" (Rainbow Wave), "off" or "default" (the keyboard's own lighting).</summary>
    public async Task SetLightsAsync(string mode, string? color, CancellationToken ct)
    {
        var args = new List<string> { "--noautoconnect", "--config", ConfigDir, "-d", DeviceName };
        switch (mode)
        {
            case "static": args.AddRange(["-m", "Static", "-c", Hex(color)]); break;
            case "rainbow": args.AddRange(["-m", "Rainbow Wave"]); break;
            case "off": args.AddRange(["-m", "Static", "-c", "000000"]); break;
            case "default": args.AddRange(["-m", "Default"]); break;
            default: throw new ArgumentException($"Unbekannter Licht-Modus '{mode}'");
        }
        if (!File.Exists(Exe)) throw new InvalidOperationException("OpenRGB ist nicht installiert");

        await _lock.WaitAsync(ct);
        try
        {
            var (code, output) = await ProcessRunner.RunAsync(Exe, args, TimeSpan.FromSeconds(30), ct);
            if (code != 0) throw new InvalidOperationException($"OpenRGB: {output.Trim()}");
        }
        finally { _lock.Release(); }
    }

    /// <summary>Only plain 6-digit hex gets through, so a value can never be mistaken for a command-line option.</summary>
    private static string Hex(string? c)
    {
        var h = (c ?? "").TrimStart('#');
        if (!HexRegex().IsMatch(h)) throw new ArgumentException($"Ungültige Farbe '{c}'");
        return h;
    }

    [GeneratedRegex("^[0-9a-fA-F]{6}$")] private static partial Regex HexRegex();
}
