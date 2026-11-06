namespace PeriHub.Core.Devices;

public interface IDevice
{
    string Id { get; }
    Task<DeviceStatus> QueryAsync(CancellationToken ct);
}
