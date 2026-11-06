namespace PeriHub.Core.Surround;

/// <summary>
/// Reader for OpenAL Soft's "MinPHR03" HRTF files (24-bit, one stored ear, single distance field).
/// Azimuth is measured clockwise from the front (90° = right); the stored ear is the LEFT one, the right ear
/// uses the mirrored azimuth. Delays are stored in quarter samples.
/// </summary>
public sealed class HrirSet
{
    public int SampleRate { get; }
    private readonly int _irSize;
    private readonly int[] _azCounts;
    private readonly int[] _offsets;
    private readonly float[][] _irs;
    private readonly byte[] _delays;

    private HrirSet(int rate, int irSize, int[] az, float[][] irs, byte[] delays)
    {
        SampleRate = rate; _irSize = irSize; _azCounts = az; _irs = irs; _delays = delays;
        _offsets = new int[az.Length + 1];
        for (int i = 0; i < az.Length; i++) _offsets[i + 1] = _offsets[i] + az[i];
    }

    public static HrirSet Load(string path)
    {
        var d = File.ReadAllBytes(path);
        if (d.Length < 20 || System.Text.Encoding.ASCII.GetString(d, 0, 8) != "MinPHR03")
            throw new InvalidDataException("Not an OpenAL MinPHR03 HRTF file");
        int rate = BitConverter.ToInt32(d, 8);
        int channelType = d[12], irSize = d[13], fields = d[14];
        if (channelType != 0 || fields != 1) throw new InvalidDataException("Unsupported HRTF layout (need mono, single field)");
        int p = 15;
        p += 2; // distance in mm, unused
        int elevations = d[p++];
        var az = new int[elevations];
        for (int i = 0; i < elevations; i++) az[i] = d[p++];
        int count = az.Sum();
        if (p + count * irSize * 3 + count != d.Length) throw new InvalidDataException("HRTF file size does not match its header");

        var irs = new float[count][];
        for (int i = 0; i < count; i++)
        {
            irs[i] = new float[irSize];
            for (int j = 0; j < irSize; j++)
            {
                int v = d[p] | d[p + 1] << 8 | d[p + 2] << 16;
                if ((v & 0x800000) != 0) v -= 1 << 24;
                irs[i][j] = v / 8388608f;
                p += 3;
            }
        }
        var delays = d.AsSpan(p, count).ToArray();
        return new HrirSet(rate, irSize, az, irs, delays);
    }

    /// <summary>Impulse response of the LEFT ear for a source at elevation 0 and the given azimuth, including its delay as leading zeros.</summary>
    public float[] LeftEarIr(double azimuthDeg)
    {
        int e = _azCounts.Length / 2; // elevation 0° is the middle row
        double az = ((azimuthDeg % 360) + 360) % 360;
        int k = (int)Math.Round(az / 360.0 * _azCounts[e]) % _azCounts[e];
        int idx = _offsets[e] + k;
        int delay = (int)Math.Round(_delays[idx] / 4.0);
        var ir = new float[delay + _irSize];
        Array.Copy(_irs[idx], 0, ir, delay, _irSize);
        return ir;
    }

    public float[] RightEarIr(double azimuthDeg) => LeftEarIr(360 - azimuthDeg);
}
