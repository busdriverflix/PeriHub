using PeriHub.Core.Devices;

namespace PeriHub.Core.Services;

/// <summary>Re-applies the saved EQ / sidetone / auto-off whenever the headset (re)connects.</summary>
public sealed class HeadsetRestorer(HeadsetDevice headset, SettingsStore store)
{
    private bool _wasConnected;

    public void OnUpdate(Snapshot snap)
    {
        var connected = snap.Devices.FirstOrDefault(d => d.Id == "headset")?.Connected == true;
        if (connected && !_wasConnected) _ = RestoreAsync();
        _wasConnected = connected;
    }

    private async Task RestoreAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2)); // give the headset a moment after it reports as connected
            var s = store.Headset;
            if (s.Eq is { Length: > 0 }) await headset.SetEqualizerAsync(s.Eq, default);
            if (s.Sidetone is { } st) await headset.SetSidetoneAsync(st, default);
            if (s.InactiveMinutes is { } m) await headset.SetInactiveTimeAsync(m, default);
            Console.WriteLine("Restored saved headset settings");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Restoring headset settings failed: {ex.Message}"); }
    }
}
