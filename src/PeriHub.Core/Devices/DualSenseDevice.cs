namespace PeriHub.Core.Devices;

/// <summary>Sony DualSense via the kernel's hid-playstation sysfs interface (no extra tools needed).</summary>
public sealed class DualSenseDevice : IDevice
{
    public string Id => "dualsense";
    private readonly Dictionary<string, DualSenseFirmwareInfo.Info?> _fwCache = new(); // read once per connection, not every poll

    public Task<DeviceStatus> QueryAsync(CancellationToken ct)
    {
        var info = new Dictionary<string, string>();
        var hid = Directory.Exists("/sys/bus/hid/devices")
            ? Directory.GetDirectories("/sys/bus/hid/devices", "*:054C:0CE6.*").FirstOrDefault()
            : null;
        if (hid is null)
            return Task.FromResult(new DeviceStatus(Id, "DualSense", false, null, "Nicht verbunden", info));

        var bus = Path.GetFileName(hid)[..4];
        info["connection"] = bus == "0003" ? "USB" : bus == "0005" ? "Bluetooth" : bus;
        info["firmware"] = Read(Path.Combine(hid, "firmware_version")) ?? "?";
        info["hardware"] = Read(Path.Combine(hid, "hardware_version")) ?? "?";
        var key = Path.GetFileName(hid);
        if (!_fwCache.TryGetValue(key, out var fw)) _fwCache[key] = fw = DualSenseFirmwareInfo.Read(hid);
        if (fw is not null) { info["updateVersion"] = $"0x{fw.UpdateVersion:x4}"; info["build"] = fw.Build; }

        int? level = null;
        var state = "Verbunden";
        var ps = Directory.GetDirectories("/sys/class/power_supply", "ps-controller-battery-*").FirstOrDefault();
        if (ps is not null)
        {
            if (int.TryParse(Read(Path.Combine(ps, "capacity")), out var c)) level = c;
            state = Read(Path.Combine(ps, "status")) switch
            {
                "Charging" => "Lädt",
                "Full" => "Voll",
                _ => "Verbunden",
            };
        }
        return Task.FromResult(new DeviceStatus(Id, "DualSense Controller", true, level, state, info));
    }

    private static string? Read(string path)
    {
        try { return File.ReadAllText(path).Trim(); } catch { return null; }
    }
}
