namespace PeriHub.Core;

public sealed record DeviceStatus(
    string Id,
    string Name,
    bool Connected,
    int? Battery,
    string BatteryState,
    Dictionary<string, string> Info);

public sealed record Snapshot(IReadOnlyList<DeviceStatus> Devices, DateTimeOffset Time);

public sealed record IpcRequest(string Cmd, Dictionary<string, string>? Args = null);

public sealed record IpcResponse(bool Ok, string? Error = null, object? Data = null);
