using System.Diagnostics;
using System.Text;
using PeriHub.Core.Services;

namespace PeriHub.Core.Surround;

/// <summary>
/// Virtual 7.1 -> binaural surround for the headset: a PipeWire filter-chain (HRTF convolution) running as a child
/// "pipewire -c" process. While running, its sink "perihub_surround" is the default output.
/// </summary>
public sealed class SurroundService : IDisposable
{
    public const string SinkName = "perihub_surround";
    private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "perihub", "surround");
    private static string ConfPath => Path.Combine(DataDir, "filter-chain.conf");
    private static string PidFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath(), "perihub-surround.pid");

    private Process? _proc;
    private string? _previousDefault;
    private readonly StringBuilder _stderr = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Running => _proc is { HasExited: false };

    public async Task StartAsync(bool swapSides, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopInnerAsync(ct);
            var headsetSink = await AudioControl.FindHeadsetSinkAsync(ct)
                ?? throw new InvalidOperationException("Headset-Audioausgang nicht gefunden (Headset an?)");

            HrirExporter.Export(Path.Combine(DataDir, "hrir"), swapSides);
            File.WriteAllText(ConfPath, BuildConfig(headsetSink, Path.Combine(DataDir, "hrir")));

            _previousDefault = (await ProcessRunner.RunAsync("pactl", ["get-default-sink"], ct: ct)).Output.Trim();
            if (_previousDefault == SinkName) _previousDefault = headsetSink;

            _stderr.Clear();
            var psi = new ProcessStartInfo("pipewire") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(ConfPath);
            _proc = Process.Start(psi) ?? throw new InvalidOperationException("pipewire konnte nicht gestartet werden");
            _proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_stderr) _stderr.AppendLine(e.Data); };
            _proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_stderr) _stderr.AppendLine(e.Data); };
            _proc.BeginErrorReadLine(); _proc.BeginOutputReadLine();
            File.WriteAllText(PidFile, _proc.Id.ToString());

            // wait until the virtual sink shows up
            for (int i = 0; i < 40; i++)
            {
                if (_proc.HasExited) break;
                var sinks = (await ProcessRunner.RunAsync("pactl", ["list", "short", "sinks"], ct: ct)).Output;
                if (sinks.Contains(SinkName)) { await ProcessRunner.RunAsync("pactl", ["set-default-sink", SinkName], ct: ct); return; }
                await Task.Delay(250, ct);
            }
            string log; lock (_stderr) log = _stderr.ToString().Trim();
            await StopInnerAsync(ct);
            throw new InvalidOperationException($"Surround-Filter ist nicht gestartet. {log}");
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await StopInnerAsync(ct); }
        finally { _gate.Release(); }
    }

    private async Task StopInnerAsync(CancellationToken ct)
    {
        // give the default output back before the filter disappears, so audio never ends up on a dead sink
        try
        {
            var current = (await ProcessRunner.RunAsync("pactl", ["get-default-sink"], ct: ct)).Output.Trim();
            if (current == SinkName)
            {
                var back = _previousDefault ?? await AudioControl.FindHeadsetSinkAsync(ct);
                if (back is not null) await ProcessRunner.RunAsync("pactl", ["set-default-sink", back], ct: ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine($"Restoring default sink failed: {ex.Message}"); }

        if (_proc is { HasExited: false }) { try { _proc.Kill(); await _proc.WaitForExitAsync(ct); } catch { } }
        _proc = null;
        try { File.Delete(PidFile); } catch { }
    }

    /// <summary>After a crash of the core the filter process could still be around; remove it so nothing is left behind.</summary>
    public static void KillLeftover()
    {
        try
        {
            if (!File.Exists(PidFile) || !int.TryParse(File.ReadAllText(PidFile), out var pid)) return;
            var cmd = File.ReadAllText($"/proc/{pid}/cmdline");
            if (cmd.Contains("filter-chain.conf")) Process.GetProcessById(pid).Kill();
            File.Delete(PidFile);
        }
        catch { /* process already gone */ }
    }

    private static string BuildConfig(string headsetSink, string hrirDir)
    {
        var channels = new[] { "FL", "FR", "FC", "LFE", "RL", "RR", "SL", "SR" }; // PipeWire's 7.1 order
        var hrirSpeakers = channels.Where(c => c != "LFE").ToArray();
        var sb = new StringBuilder();
        foreach (var c in channels) sb.AppendLine($"                    {{ type = builtin label = copy name = copy{c} }}");
        foreach (var c in hrirSpeakers)
            foreach (var ear in new[] { "L", "R" })
                sb.AppendLine($"                    {{ type = builtin label = convolver name = conv{c}_{ear} config = {{ filename = \"{hrirDir}/hrir_{c}_{ear}.wav\" }} }}");
        sb.AppendLine("                    { type = builtin label = mixer name = mixL }");
        sb.AppendLine("                    { type = builtin label = mixer name = mixR }");
        var nodes = sb.ToString();

        sb.Clear();
        int input = 1;
        foreach (var c in channels)
        {
            if (c == "LFE")
            { // no HRTF for the subwoofer channel: straight to both ears
                sb.AppendLine($"                    {{ output = \"copyLFE:Out\" input = \"mixL:In {input}\" }}");
                sb.AppendLine($"                    {{ output = \"copyLFE:Out\" input = \"mixR:In {input}\" }}");
            }
            else
            {
                sb.AppendLine($"                    {{ output = \"copy{c}:Out\" input = \"conv{c}_L:In\" }}");
                sb.AppendLine($"                    {{ output = \"copy{c}:Out\" input = \"conv{c}_R:In\" }}");
                sb.AppendLine($"                    {{ output = \"conv{c}_L:Out\" input = \"mixL:In {input}\" }}");
                sb.AppendLine($"                    {{ output = \"conv{c}_R:Out\" input = \"mixR:In {input}\" }}");
            }
            input++;
        }
        var links = sb.ToString();
        var inputs = string.Join(" ", channels.Select(c => $"\"copy{c}:In\""));

        return $$"""
            context.properties = { log.level = 0 }
            context.spa-libs = {
                audio.convert.* = audioconvert/libspa-audioconvert
                support.*       = support/libspa-support
            }
            context.modules = [
                { name = libpipewire-module-rt args = { nice.level = -11 } flags = [ ifexists nofail ] }
                { name = libpipewire-module-protocol-native }
                { name = libpipewire-module-client-node }
                { name = libpipewire-module-adapter }
                { name = libpipewire-module-filter-chain
                    args = {
                        node.description = "PeriHub Surround"
                        media.name = "PeriHub Surround"
                        filter.graph = {
                            nodes = [
            {{nodes}}                ]
                            links = [
            {{links}}                ]
                            inputs = [ {{inputs}} ]
                            outputs = [ "mixL:Out" "mixR:Out" ]
                        }
                        capture.props = {
                            node.name = "{{SinkName}}"
                            media.class = Audio/Sink
                            audio.channels = 8
                            audio.position = [ FL FR FC LFE RL RR SL SR ]
                        }
                        playback.props = {
                            node.name = "{{SinkName}}_output"
                            node.passive = true
                            audio.channels = 2
                            audio.position = [ FL FR ]
                            target.object = "{{headsetSink}}"
                        }
                    }
                }
            ]
            """;
    }

    public void Dispose() { try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { } }
}
