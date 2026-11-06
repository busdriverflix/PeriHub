using System.Net.Sockets;
using System.Text.Json;

namespace PeriHub.Gui;

/// <summary>Talks JSON-lines to the PeriHub core over its Unix socket. One short connection per call.</summary>
public sealed class CoreClient
{
    private static string SocketPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(), "perihub.sock");

    public async Task<JsonElement> CallAsync(string cmd, Dictionary<string, string>? args = null)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90)); // update check can be slow
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token);
        using var stream = new NetworkStream(socket);
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { cmd, args }));
        var line = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("Core closed the connection");
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement.Clone();
        if (!root.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException(root.GetProperty("error").GetString());
        return root;
    }
}
