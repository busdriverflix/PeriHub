namespace PeriHub.Core.Tray;

/// <summary>Renders a small ARGB32 tray icon showing a number (battery %) on a colored rounded square.</summary>
public static class IconRenderer
{
    public const int Size = 24;

    // 3x5 pixel digits, one row per int (3 bits, MSB = left)
    private static readonly int[][] Digits =
    [
        [7,5,5,5,7], [2,6,2,2,7], [7,1,7,4,7], [7,1,7,1,7], [5,5,7,1,1],
        [7,4,7,1,7], [7,4,7,5,7], [7,1,1,1,1], [7,5,7,5,7], [7,5,7,1,7],
    ];
    private static readonly int[] Dash = [0, 0, 7, 0, 0];

    public static byte[] Render(int? percent)
    {
        var px = new byte[Size * Size * 4]; // D-Bus wants ARGB, byte order A,R,G,B
        var (r, g, b) = percent switch
        {
            null => (110, 110, 110),
            <= 10 => (200, 50, 50),
            <= 25 => (210, 150, 30),
            _ => (50, 160, 80),
        };
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var inCorner = (x < 3 || x >= Size - 3) && (y < 3 || y >= Size - 3)
                    && Math.Pow(Math.Min(x, Size - 1 - x) - 3, 2) + Math.Pow(Math.Min(y, Size - 1 - y) - 3, 2) > 9;
                if (inCorner) continue;
                Set(px, x, y, 255, r, g, b);
            }

        var text = percent is { } p ? p.ToString() : "-";
        var scale = text.Length >= 3 ? 2 : 3;
        int glyphW = 3 * scale, gap = scale, totalW = text.Length * glyphW + (text.Length - 1) * gap, totalH = 5 * scale;
        int ox = (Size - totalW) / 2, oy = (Size - totalH) / 2;
        for (int i = 0; i < text.Length; i++)
        {
            var rows = text[i] == '-' ? Dash : Digits[text[i] - '0'];
            for (int ry = 0; ry < 5; ry++)
                for (int rx = 0; rx < 3; rx++)
                    if ((rows[ry] >> (2 - rx) & 1) != 0)
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                Set(px, ox + i * (glyphW + gap) + rx * scale + dx, oy + ry * scale + dy, 255, 255, 255, 255);
        }
        return px;
    }

    private static void Set(byte[] px, int x, int y, int a, int r, int g, int b)
    {
        if ((uint)x >= Size || (uint)y >= Size) return;
        var i = (y * Size + x) * 4;
        px[i] = (byte)a; px[i + 1] = (byte)r; px[i + 2] = (byte)g; px[i + 3] = (byte)b;
    }
}
