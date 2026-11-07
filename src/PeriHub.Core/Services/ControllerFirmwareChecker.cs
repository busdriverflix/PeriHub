using System.Globalization;
using System.Text.Json;

namespace PeriHub.Core.Services;

public sealed record ControllerFirmwareResult(string? Installed, string? Latest, string Status, string? Error, string SupportUrl);

/// <summary>
/// Asks Sony's update server which DualSense firmware is the newest. It is the same small JSON file Sony's own Windows
/// updater reads; it only carries version numbers, no release notes and no download links.
/// </summary>
public sealed class ControllerFirmwareChecker
{
    public const string InfoUrl = "https://fwupdater.dl.playstation.net/fwupdater/info.json";
    public const string SupportUrl = "https://www.playstation.com/en-us/support/hardware/accessories/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<ControllerFirmwareResult> CheckAsync(string? installedHex, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, InfoUrl);
            req.Headers.UserAgent.ParseAdd("PeriHub/1.0"); // generic client name, nothing about the user
            using var resp = await Http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            // The file has one key per controller family; keys 0004 and 000B are the ones matching the standard DualSense
            // (mine reports 0x0630, the value of both). Which key belongs to which model is not documented by Sony.
            var latest = new[] { "FwUpdate0004LatestVersion", "FwUpdate000BLatestVersion" }
                .Select(k => doc.RootElement.TryGetProperty(k, out var v) ? Parse(v.GetString()) : null)
                .Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty(-1).Max();
            if (latest < 0) return new(installedHex, null, "unknown", "Sonys Datei enthält keine bekannte Versionsangabe", SupportUrl);

            var latestHex = $"0x{latest:x4}";
            var installed = Parse(installedHex);
            if (installed is null) return new(installedHex, latestHex, "unknown", "Installierte Version nicht lesbar", SupportUrl);
            var status = installed < latest ? "newer" : installed == latest ? "current" : "ahead";
            return new(installedHex, latestHex, status, null, SupportUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(installedHex, null, "unknown", $"Sony-Server nicht erreichbar: {ex.Message}", SupportUrl);
        }
    }

    private static int? Parse(string? hex) =>
        hex is not null && int.TryParse(hex.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
}
