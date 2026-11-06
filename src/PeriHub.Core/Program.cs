using PeriHub.Core;
using System.Text.Json;
using PeriHub.Core.Ipc;
using PeriHub.Core.Services;
using PeriHub.Core.Tray;

var devices = new DeviceManager();
var notifier = new BatteryNotifier();
var updates = new UpdateChecker();

if (args.Contains("--once")) // debugging: print one status snapshot and exit
{
    await devices.RefreshAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(devices.Current, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

devices.Updated += notifier.OnUpdate;

using var tray = new StatusNotifierTray { OnActivate = StatusNotifierTray.LaunchGui };
if (!args.Contains("--no-tray"))
{
    await tray.StartAsync(cts.Token);
    devices.Updated += snap =>
    {
        var headset = snap.Devices.FirstOrDefault(d => d.Id == "headset");
        var pad = snap.Devices.FirstOrDefault(d => d.Id == "dualsense");
        var lines = snap.Devices.Select(d => d.Connected
            ? $"{d.Name}: {(d.Battery is { } b ? b + " %" : "?")} ({d.BatteryState})" : $"{d.Name}: {d.BatteryState}");
        // menu lines only when the device is connected and reports a battery level
        static string? MenuLine(DeviceStatus? d, string label) =>
            d is { Connected: true, Battery: { } b } ? $"{label}: {b} %{(d.BatteryState == "Lädt" ? " (lädt)" : "")}" : null;
        tray.Update(headset?.Connected == true ? headset.Battery : null, string.Join("\n", lines),
            MenuLine(headset, "Headset"), MenuLine(pad, "Controller"));
    };
}
Console.WriteLine($"PeriHub core running, socket: {IpcServer.SocketPath}");
_ = StartupUpdateCheckAsync(updates, cts.Token);
await Task.WhenAll(
    devices.RunAsync(TimeSpan.FromSeconds(30), cts.Token),
    new IpcServer(devices, updates).RunAsync(cts.Token));

/// <summary>Shortly after start, ask fwupd about our peripherals and notify only if something is actually available.</summary>
static async Task StartupUpdateCheckAsync(UpdateChecker updates, CancellationToken ct)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(60), ct); // let the desktop and network settle after login
        foreach (var id in new[] { "headset", "dualsense", "keyboard", "mouse" })
        {
            var json = JsonSerializer.SerializeToElement(await updates.CheckAsync(id, ct));
            if (json.TryGetProperty("updates", out var n) && n.GetInt32() > 0)
                await ProcessRunner.RunAsync("notify-send", ["-a", "PeriHub", "-i", "software-update-available",
                    "Firmware-Update verfügbar", $"Für ein Gerät ({id}) gibt es ein Update. Details in PeriHub."], ct: ct);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { Console.Error.WriteLine($"Startup update check failed: {ex.Message}"); }
}
