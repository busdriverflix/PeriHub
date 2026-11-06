using System.Net.Sockets;
using System.Text.Json;
using PeriHub.Core.Services;

namespace PeriHub.Core.Ipc;

/// <summary>JSON-lines protocol over a Unix socket in $XDG_RUNTIME_DIR. One request line -> one response line.</summary>
public sealed class IpcServer(DeviceManager devices, UpdateChecker updates)
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
                await devices.Headset.SetEqualizerAsync(a["bands"].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList(), ct);
                return new(true);
            case "headset.sidetone":
                await devices.Headset.SetSidetoneAsync(int.Parse(a["level"]), ct);
                return new(true);
            case "headset.inactive":
                await devices.Headset.SetInactiveTimeAsync(int.Parse(a["minutes"]), ct);
                return new(true);
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
