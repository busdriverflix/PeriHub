using PeriHub.Core.Devices;

namespace PeriHub.Core.Services;

/// <summary>Re-applies the saved keyboard lighting when the keyboard shows up (also once at core start).</summary>
public sealed class KeyboardRestorer(KeyboardDevice keyboard, SettingsStore store)
{
    private bool _wasConnected;

    public void OnUpdate(Snapshot snap)
    {
        var connected = snap.Devices.FirstOrDefault(d => d.Id == "keyboard")?.Connected == true;
        if (connected && !_wasConnected) _ = RestoreAsync();
        _wasConnected = connected;
    }

    private async Task RestoreAsync()
    {
        try
        {
            var s = store.Keyboard;
            if (s.Mode is null) return; // user never set lighting: leave the keyboard as it is
            await keyboard.SetLightsAsync(s.Mode, s.Color, default);
            Console.WriteLine("Restored saved keyboard lighting");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Restoring keyboard lighting failed: {ex.Message}"); }
    }
}
