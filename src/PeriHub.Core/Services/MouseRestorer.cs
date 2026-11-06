using PeriHub.Core.Devices;

namespace PeriHub.Core.Services;

/// <summary>Re-applies the saved mouse lighting whenever the mouse wakes up / reconnects.</summary>
public sealed class MouseRestorer(MouseDevice mouse, SettingsStore store)
{
    private bool _wasConnected;

    public void OnUpdate(Snapshot snap)
    {
        var connected = snap.Devices.FirstOrDefault(d => d.Id == "mouse")?.Connected == true;
        if (connected && !_wasConnected) _ = RestoreAsync();
        _wasConnected = connected;
    }

    private async Task RestoreAsync()
    {
        try
        {
            var s = store.Mouse;
            if (s.Mode is null) return; // user never set lighting: leave the mouse as it is
            await mouse.SetLightsAsync(s.Mode, s.Top, s.Middle, s.Bottom, default);
            Console.WriteLine("Restored saved mouse lighting");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Restoring mouse lighting failed: {ex.Message}"); }
    }
}
