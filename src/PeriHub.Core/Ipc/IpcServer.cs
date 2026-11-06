using System.Net.Sockets;
using System.Text.Json;
using PeriHub.Core.Services;

namespace PeriHub.Core.Ipc;

/// <summary>JSON-lines protocol over a Unix socket in $XDG_RUNTIME_DIR. One request line -> one response line.</summary>
public sealed class IpcServer(DeviceManager devices, UpdateChecker updates, SettingsStore settings, PeriHub.Core.Surround.SurroundService surround)
{
    public static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(), "perihub.sock");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken ct)
    {
        if (File.Exists(SocketPath)) File.Delete(SocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // only the current user
        listener.Listen(8);
        try
        {
            while (!ct.IsCancellationRequested)
                _ = HandleAsync(await listener.AcceptAsync(ct), ct);
        }
        catch (OperationCanceledException) { }
        finally { try { File.Delete(SocketPath); } catch { } }
    }

    private async Task HandleAsync(Socket client, CancellationToken ct)
    {
        using var _ = client;
        using var stream = new NetworkStream(client);
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        try
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                IpcResponse resp;
                try
                {
                    var req = JsonSerializer.Deserialize<IpcRequest>(line, Json) ?? throw new InvalidOperationException("empty request");
                    resp = await DispatchAsync(req, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { resp = new IpcResponse(false, ex.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp, Json));
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    private async Task<IpcResponse> DispatchAsync(IpcRequest req, CancellationToken ct)
    {
        var a = req.Args ?? new();
        switch (req.Cmd)
        {
            case "status":
                return new(true, Data: devices.Current);
            case "refresh":
                await devices.RefreshAsync(ct);
                return new(true, Data: devices.Current);
            case "headset.preset":
                await devices.Headset.SetPresetAsync(int.Parse(a["index"]), ct);
                return new(true);
            case "headset.eq":
            {
                var bands = a["bands"].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                await devices.Headset.SetEqualizerAsync(bands, ct);
                settings.UpdateHeadset(h => h with { Eq = bands }); // only saved once the headset accepted it
                return new(true);
            }
            case "headset.sidetone":
            {
                var level = int.Parse(a["level"]);
                await devices.Headset.SetSidetoneAsync(level, ct);
                settings.UpdateHeadset(h => h with { Sidetone = level });
                return new(true);
            }
            case "headset.inactive":
            {
                var minutes = int.Parse(a["minutes"]);
                await devices.Headset.SetInactiveTimeAsync(minutes, ct);
                settings.UpdateHeadset(h => h with { InactiveMinutes = minutes });
                return new(true);
            }
            case "surround.get":
                return new(true, Data: new { enabled = settings.Surround.Enabled, running = surround.Running, swapSides = settings.Surround.SwapSides });
            case "surround.set":
            {
                var enable = bool.Parse(a["enabled"]);
                var swap = a.TryGetValue("swapSides", out var sw) ? bool.Parse(sw) : settings.Surround.SwapSides;
                if (enable) await surround.StartAsync(swap, ct); else await surround.StopAsync(ct);
                settings.UpdateSurround(_ => new(enable, swap)); // saved only after it worked
                return new(true);
            }
            case "mouse.get":
                return new(true, Data: settings.Mouse);
            case "mouse.lights":
            {
                var mode = a["mode"]; a.TryGetValue("top", out var top); a.TryGetValue("middle", out var mid); a.TryGetValue("bottom", out var bot);
                await devices.Mouse.SetLightsAsync(mode, top, mid, bot, ct);
                settings.UpdateMouse(_ => new(mode, top, mid, bot));
                return new(true);
            }
            case "settings.get":
                return new(true, Data: settings.Headset);
            case "audio.get":
                return new(true, Data: new { volume = await AudioControl.GetVolumeAsync(ct) });
            case "audio.volume":
                await AudioControl.SetVolumeAsync(int.Parse(a["percent"]), ct);
                return new(true);
            case "updates.check":
                return new(true, Data: await updates.CheckAsync(a.GetValueOrDefault("device"), ct));
            default:
                return new(false, $"unknown command '{req.Cmd}'");
        }
    }
}
