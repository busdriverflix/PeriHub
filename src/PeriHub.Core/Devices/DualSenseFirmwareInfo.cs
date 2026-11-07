using System.Runtime.InteropServices;
using System.Text;

namespace PeriHub.Core.Devices;

/// <summary>
/// Reads the DualSense's firmware info feature report (0x20) through hidraw. This is a read-only query.
/// Sony's updater compares the "update version" (u16 at byte 44, e.g. 0x0630), not the kernel's firmware_version.
/// </summary>
public static class DualSenseFirmwareInfo
{
    public sealed record Info(int UpdateVersion, string Build);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(nint fd, nuint request, byte[] data);

    public static Info? Read(string hidDeviceDir)
    {
        try
        {
            var node = Directory.GetDirectories(Path.Combine(hidDeviceDir, "hidraw"), "hidraw*").FirstOrDefault();
            if (node is null) return null;
            using var handle = File.OpenHandle("/dev/" + Path.GetFileName(node), FileMode.Open, FileAccess.ReadWrite);

            const int size = 64;
            var buf = new byte[size];
            buf[0] = 0x20;
            // HIDIOCGFEATURE(size): _IOC(read|write, 'H', 0x07, size)
            nuint request = (3u << 30) | ((uint)size << 16) | ((uint)'H' << 8) | 0x07;
            if (ioctl(handle.DangerousGetHandle(), request, buf) < 0) return null;
            if (buf[0] != 0x20) return null;

            var update = buf[44] | buf[45] << 8;
            var date = Encoding.ASCII.GetString(buf, 1, 11).Replace('\0', ' ').Trim();
            var time = Encoding.ASCII.GetString(buf, 12, 8).Replace('\0', ' ').Trim();
            return new Info(update, $"{string.Join(' ', date.Split(' ', StringSplitOptions.RemoveEmptyEntries))} {time}".Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // no permission or controller just disconnected: simply no extra info
        }
    }
}
