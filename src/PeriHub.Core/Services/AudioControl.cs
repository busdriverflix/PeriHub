using System.Text.RegularExpressions;

namespace PeriHub.Core.Services;

/// <summary>Volume of the headset's PipeWire sink, via pactl.</summary>
public static partial class AudioControl
{
    public static async Task<string?> FindHeadsetSinkAsync(CancellationToken ct)
    {
        var (_, output) = await ProcessRunner.RunAsync("pactl", ["list", "short", "sinks"], ct: ct);
        return output.Split('\n').Select(l => l.Split('\t'))
            .Where(c => c.Length > 1 && c[1].Contains("Logitech_PRO_X_2", StringComparison.OrdinalIgnoreCase))
            .Select(c => c[1]).FirstOrDefault();
    }

    public static async Task<int?> GetVolumeAsync(CancellationToken ct)
    {
        var sink = await FindHeadsetSinkAsync(ct);
        if (sink is null) return null;
        var (_, output) = await ProcessRunner.RunAsync("pactl", ["get-sink-volume", sink], ct: ct);
        var m = PercentRegex().Match(output);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static async Task SetVolumeAsync(int percent, CancellationToken ct)
    {
        var sink = await FindHeadsetSinkAsync(ct) ?? throw new InvalidOperationException("Headset-Audioausgang nicht gefunden");
        await ProcessRunner.RunAsync("pactl", ["set-sink-volume", sink, $"{Math.Clamp(percent, 0, 100)}%"], ct: ct);
    }

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex PercentRegex();
}
