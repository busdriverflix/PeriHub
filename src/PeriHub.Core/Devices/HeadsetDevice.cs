using System.Globalization;
using System.Text.Json;
using PeriHub.Core.Services;

namespace PeriHub.Core.Devices;

/// <summary>Logitech PRO X 2 via the headsetcontrol CLI (hardware EQ, battery, sidetone).</summary>
public sealed class HeadsetDevice : IDevice
{
    public string Id => "headset";
    private readonly SemaphoreSlim _lock = new(1, 1); // the HID device must not be hit concurrently

    public async Task<DeviceStatus> QueryAsync(CancellationToken ct)
    {
        var info = new Dictionary<string, string>();
        try
        {
            var (_, json) = await RunLocked(["-o", "json"], ct);
            using var doc = JsonDocument.Parse(json);
            var dev = doc.RootElement.GetProperty("devices").EnumerateArray().FirstOrDefault();
            if (dev.ValueKind != JsonValueKind.Object)
                return new DeviceStatus(Id, "Logitech PRO X 2", false, null, "Nicht gefunden", info);

            var name = dev.GetProperty("device").GetString() ?? "Headset";
            var bat = dev.GetProperty("battery");
            var state = bat.GetProperty("status").GetString() ?? "";
            int? level = bat.TryGetProperty("level", out var l) && l.TryGetInt32(out var lv) && lv >= 0 ? lv : null;
            var connected = state is "BATTERY_AVAILABLE" or "BATTERY_CHARGING";
            if (dev.TryGetProperty("equalizer_presets", out var presets))
                info["presets"] = string.Join(",", presets.EnumerateObject().Select(p => p.Name));
            return new DeviceStatus(Id, name, connected, connected ? level : null, state switch
            {
                "BATTERY_CHARGING" => "Lädt",
                "BATTERY_AVAILABLE" => "Verbunden",
                _ => "Headset aus / nicht verbunden",
            }, info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DeviceStatus(Id, "Logitech PRO X 2", false, null, $"Fehler: {ex.Message}", info);
        }
    }

    public Task SetEqualizerAsync(IReadOnlyList<double> bands, CancellationToken ct) =>
        Run(["-e", string.Join(",", bands.Select(b => b.ToString(CultureInfo.InvariantCulture)))], ct);

    public Task SetPresetAsync(int preset, CancellationToken ct) => Run(["-p", preset.ToString()], ct);
    public Task SetSidetoneAsync(int level, CancellationToken ct) => Run(["-s", Math.Clamp(level, 0, 128).ToString()], ct);
    public Task SetInactiveTimeAsync(int minutes, CancellationToken ct) => Run(["-i", Math.Clamp(minutes, 0, 90).ToString()], ct);

    private async Task Run(string[] args, CancellationToken ct)
    {
        var (code, output) = await RunLocked(args, ct);
        if (code != 0) throw new InvalidOperationException($"headsetcontrol: {output.Trim()}");
    }

    private async Task<(int, string)> RunLocked(string[] args, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return await ProcessRunner.RunAsync("headsetcontrol", args, ct: ct); }
        finally { _lock.Release(); }
    }
}
