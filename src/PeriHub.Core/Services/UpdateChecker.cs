using System.Text.Json;

namespace PeriHub.Core.Services;

/// <summary>Checks for firmware updates via fwupd (LVFS). Never flashes anything by itself.</summary>
public sealed class UpdateChecker(DeviceManager devices, ControllerFirmwareChecker controllerChecker)
{
    /// <summary>Keywords that identify each of our devices in fwupd's name/vendor strings.</summary>
    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["headset"] = ["pro x 2", "logitech"],
        ["dualsense"] = ["dualsense", "wireless controller", "sony"],
        ["keyboard"] = ["vulcan", "roccat"],
        ["mouse"] = ["aerox", "steelseries"],
    };

    public async Task<Dictionary<string, object?>> CheckAsync(string? device, CancellationToken ct)
    {
        string[]? words = null;
        if (device is not null && !Keywords.TryGetValue(device, out words))
            return new() { ["source"] = "fwupd", ["error"] = $"Unbekanntes Gerät '{device}'" };
        try
        {
            await ProcessRunner.RunAsync("fwupdmgr", ["refresh", "--assume-yes"], TimeSpan.FromSeconds(60), ct); // exit code 2 = already up to date
            var known = await ListAsync(["get-devices", "--json"], ct);
            var updates = await ListAsync(["get-updates", "--json", "--no-unreported-check"], ct);

            bool Match((string Name, string Vendor) d) =>
                words is null || words.Any(w => d.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || d.Vendor.Contains(w, StringComparison.OrdinalIgnoreCase));

            var hits = updates.Where(Match).Select(d => d.Name).ToList();
            var result = new Dictionary<string, object?>
            {
                ["source"] = "fwupd",
                ["supported"] = words is null || known.Any(Match), // does fwupd know the device at all?
            };

            // the DualSense is not in fwupd; Sony's own version list is the source for it
            if (device is null or "dualsense")
            {
                var installed = devices.Current.Devices.FirstOrDefault(d => d.Id == "dualsense" && d.Connected)?.Info.GetValueOrDefault("updateVersion");
                if (installed is not null)
                {
                    var fw = await controllerChecker.CheckAsync(installed, ct);
                    result["controller"] = fw;
                    if (fw.Status == "newer") hits.Add($"DualSense-Firmware {fw.Latest} (installiert {fw.Installed})");
                }
            }
            result["updates"] = hits.Count;
            result["devices"] = hits;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new() { ["source"] = "fwupd", ["updates"] = 0, ["error"] = ex.Message };
        }
    }

    private static async Task<List<(string Name, string Vendor)>> ListAsync(string[] args, CancellationToken ct)
    {
        var (_, output) = await ProcessRunner.RunAsync("fwupdmgr", args, TimeSpan.FromSeconds(60), ct);
        var list = new List<(string, string)>();
        var start = output.IndexOf('{');
        if (start < 0) return list;
        try
        {
            using var doc = JsonDocument.Parse(output[start..]);
            if (!doc.RootElement.TryGetProperty("Devices", out var devs)) return list;
            foreach (var d in devs.EnumerateArray())
                list.Add((Str(d, "Name"), Str(d, "Vendor")));
        }
        catch (JsonException) { /* fwupd printed a human-readable message instead of JSON, e.g. "no updates" */ }
        return list;

        static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    }
}
