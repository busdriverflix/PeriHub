using PeriHub.Core.Devices;

namespace PeriHub.Core.Services;

public sealed class DeviceManager
{
    public HeadsetDevice Headset { get; } = new();
    public MouseDevice Mouse { get; } = new();
    public KeyboardDevice Keyboard { get; } = new();
    private readonly List<IDevice> _devices;
    public Snapshot Current { get; private set; } = new([], DateTimeOffset.MinValue);
    public event Action<Snapshot>? Updated;

    public DeviceManager() => _devices = [Headset, new DualSenseDevice(), Mouse, Keyboard];

    public async Task RefreshAsync(CancellationToken ct)
    {
        var all = await Task.WhenAll(_devices.Select(d => d.QueryAsync(ct)));
        Current = new Snapshot(all, DateTimeOffset.Now);
        Updated?.Invoke(Current);
    }

    public async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        do { try { await RefreshAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine(ex); } }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
