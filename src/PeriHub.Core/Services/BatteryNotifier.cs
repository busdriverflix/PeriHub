namespace PeriHub.Core.Services;

/// <summary>Sends a desktop notification when a battery crosses 20 / 10 / 5 %.</summary>
public sealed class BatteryNotifier
{
    private static readonly int[] Thresholds = [20, 10, 5];
    private readonly Dictionary<string, int> _lastNotified = new(); // deviceId -> threshold already announced

    public void OnUpdate(Snapshot snap)
    {
        foreach (var d in snap.Devices)
        {
            if (d.Battery is not { } level || !d.Connected || d.BatteryState == "Lädt")
            {
                if (d.BatteryState == "Lädt" || !d.Connected) _lastNotified.Remove(d.Id);
                continue;
            }
            var crossed = Thresholds.Where(t => level <= t).DefaultIfEmpty(int.MaxValue).Min();
            if (crossed == int.MaxValue) { _lastNotified.Remove(d.Id); continue; }
            if (_lastNotified.TryGetValue(d.Id, out var prev) && prev <= crossed) continue;

            _lastNotified[d.Id] = crossed;
            _ = Notify(d.Name, level, critical: crossed <= 10);
        }
    }

    private static async Task Notify(string name, int level, bool critical)
    {
        try
        {
            await ProcessRunner.RunAsync("notify-send", [
                "-a", "PeriHub", "-u", critical ? "critical" : "normal", "-i", "battery-caution",
                $"{name}: Akku bei {level} %", "Bitte bald aufladen."]);
        }
        catch (Exception ex) { Console.Error.WriteLine($"notify-send failed: {ex.Message}"); }
    }
}
