namespace PeriHub.Core.Surround;

/// <summary>Writes one mono float32 WAV per (speaker, ear) so PipeWire's convolver can use them.</summary>
public static class HrirExporter
{
    public const string SystemHrtf = "/usr/share/openal/hrtf/Default HRTF.mhr";

    // virtual speaker positions, degrees clockwise from the front (standard 7.1 layout)
    public static readonly (string Name, double Azimuth)[] Speakers =
        [("FL", 330), ("FR", 30), ("FC", 0), ("RL", 210), ("RR", 150), ("SL", 270), ("SR", 90)];

    public static void Export(string dir, bool swapSides, string? hrtfPath = null)
    {
        var set = HrirSet.Load(hrtfPath ?? SystemHrtf);
        Directory.CreateDirectory(dir);

        // one global gain so the centre speaker's left-ear response has a fixed energy; keeps relative levels between speakers intact
        double centreEnergy = set.LeftEarIr(0).Sum(x => (double)x * x);
        float gain = (float)(0.6 / Math.Sqrt(centreEnergy));

        foreach (var (name, azimuth) in Speakers)
        {
            var az = swapSides ? 360 - azimuth : azimuth;
            WriteWav(Path.Combine(dir, $"hrir_{name}_L.wav"), set.LeftEarIr(az), gain, set.SampleRate);
            WriteWav(Path.Combine(dir, $"hrir_{name}_R.wav"), set.RightEarIr(az), gain, set.SampleRate);
        }
    }

    private static void WriteWav(string path, float[] samples, float gain, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = samples.Length * 4;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)1); // IEEE float, mono
        w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)32);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s * gain);
    }
}
