using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PeriHub.Gui;

public partial class MainWindow : Window
{
    /// <summary>One device tile in the list plus its detail pane.</summary>
    private sealed record Tile(string Id, string Title, string IconKey, Control Pane, Button UpdateButton, TextBlock UpdateResult)
    {
        public TextBlock Sub { get; init; } = new() { FontSize = 12, Opacity = 0.7 };
        public TextBlock Pct { get; init; } = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        public StackPanel Info { get; init; } = new();
    }

    private readonly CoreClient _core = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Slider[] _bands;
    private readonly List<Tile> _tiles = [];
    private readonly Dictionary<string, DispatcherTimer> _debounce = new();
    private bool _loading;          // true while we set controls from code, so no command is sent back
    private bool _presetsFilled;
    private bool _compact;
    private double _expandedWidth = 940;

    // volume is sent immediately while dragging, but strictly one request at a time (latest value wins)
    private bool _volBusy;
    private int _volPending = -1;

    public MainWindow()
    {
        InitializeComponent();
        _bands = [Band0, Band1, Band2, Band3, Band4];

        _tiles.Add(new("headset", "Logitech PRO X 2", "IconHeadset", HeadsetPane, HeadsetUpdateButton, HeadsetUpdateResult));
        _tiles.Add(new("dualsense", "DualSense", "IconGamepad", PadPane, PadUpdateButton, PadUpdateResult));
        _tiles.Add(new("keyboard", "ROCCAT Vulcan II", "IconKeyboard", KeyboardPane, KeyboardUpdateButton, KeyboardUpdateResult));
        _tiles.Add(new("mouse", "Aerox 5 Wireless", "IconMouse", MousePane, MouseUpdateButton, MouseUpdateResult));
        foreach (var t in _tiles) BuildTile(t);
        DeviceList.SelectionChanged += (_, _) => ShowSelectedPane();
        DeviceList.SelectedIndex = 0;
        SidetoneLabel.Text = "?"; // the headset cannot report its current sidetone

        foreach (var (label, minutes) in new[] { ("Nie", 0), ("5 Minuten", 5), ("15 Minuten", 15), ("30 Minuten", 30), ("60 Minuten", 60), ("90 Minuten", 90) })
            InactiveBox.Items.Add(new ComboBoxItem { Content = label, Tag = minutes });

        VolumeSlider.ValueChanged += (_, _) =>
        {
            VolumeLabel.Text = $"{(int)VolumeSlider.Value} %";
            if (!_loading) PushVolume((int)VolumeSlider.Value);
        };
        SidetoneSlider.ValueChanged += (_, _) => { SidetoneLabel.Text = ((int)SidetoneSlider.Value).ToString(); Debounced("side", () => Send("headset.sidetone", ("level", ((int)SidetoneSlider.Value).ToString()))); };
        foreach (var b in _bands) b.ValueChanged += (_, _) => Debounced("eq", SendEqualizer);
        PresetBox.SelectionChanged += (_, _) =>
        {
            if (_loading || PresetBox.SelectedIndex < 0) return;
            Send("headset.preset", ("index", PresetBox.SelectedIndex.ToString()));
        };
        InactiveBox.SelectionChanged += (_, _) =>
        {
            if (_loading || InactiveBox.SelectedItem is not ComboBoxItem { Tag: int m }) return;
            Send("headset.inactive", ("minutes", m.ToString()));
        };

        UpdateAllButton.Click += async (_, _) => await RunUpdateCheck(UpdateAllButton, UpdateAllResult, null);
        foreach (var t in _tiles)
        {
            var tile = t;
            tile.UpdateButton.Click += async (_, _) => await RunUpdateCheck(tile.UpdateButton, tile.UpdateResult, tile.Id);
        }
        CollapseButton.Click += (_, _) => SetCompact(!_compact);

        _poll.Tick += async (_, _) => await RefreshAsync();
        _poll.Start();
        Opened += async (_, _) => await RefreshAsync();
    }

    // ---------- tiles ----------

    private void BuildTile(Tile t)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = (Geometry)this.FindResource(t.IconKey)!,
            Stroke = new SolidColorBrush(Color.Parse("#6FA8DC")), StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = 30, Height = 30,
        };
        t.Info.Margin = new Thickness(12, 0, 0, 0);
        t.Info.VerticalAlignment = VerticalAlignment.Center;
        t.Info.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeight.SemiBold });
        t.Info.Children.Add(t.Sub);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { icon, t.Info } };
        DeviceList.Items.Add(new ListBoxItem
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Children = { row, t.Pct } },
        });
        t.Sub.Text = t.Id is "keyboard" or "mouse" ? "Noch keine Steuerung" : "…";
    }

    private void ShowSelectedPane()
    {
        var sel = DeviceList.SelectedIndex;
        for (int i = 0; i < _tiles.Count; i++) _tiles[i].Pane.IsVisible = i == sel;
    }

    private void SetCompact(bool compact)
    {
        _compact = compact;
        if (compact) _expandedWidth = Width;
        DetailArea.IsVisible = !compact;
        LeftPanel.Width = compact ? 84 : 260;
        UpdateAllButton.Content = compact ? "⟳" : "Nach Updates suchen";
        UpdateAllResult.IsVisible = !compact;
        foreach (var t in _tiles) { t.Info.IsVisible = !compact; t.Pct.IsVisible = compact; }
        CollapseButton.Content = compact ? "›" : "‹";
        MinWidth = compact ? 0 : 130;
        Width = compact ? 84 + 12 + 28 + 12 + 24 : _expandedWidth;
    }

    // ---------- commands ----------

    private async Task RunUpdateCheck(Button button, TextBlock result, string? device)
    {
        button.IsEnabled = false; result.Text = "Suche …";
        try
        {
            var data = (await _core.CallAsync("updates.check", device is null ? null : new() { ["device"] = device })).GetProperty("data");
            if (data.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) result.Text = $"Fehler: {e.GetString()}";
            else if (device is not null && data.TryGetProperty("supported", out var s) && !s.GetBoolean()) result.Text = "fwupd kennt dieses Gerät nicht, automatische Prüfung nicht möglich.";
            else if (data.GetProperty("updates").GetInt32() is var n && n == 0) result.Text = "Kein Update gefunden.";
            else result.Text = $"{n} Update(s): {string.Join(", ", data.GetProperty("devices").EnumerateArray().Select(x => x.GetString()))}";
        }
        catch (Exception ex) { result.Text = $"Fehler: {ex.Message}"; }
        button.IsEnabled = true;
    }

    private async void PushVolume(int percent)
    {
        if (_volBusy) { _volPending = percent; return; }
        _volBusy = true;
        try
        {
            while (percent >= 0)
            {
                _volPending = -1;
                await _core.CallAsync("audio.volume", new() { ["percent"] = percent.ToString() });
                percent = _volPending;
            }
        }
        catch (Exception ex) { ConnectionBanner.Text = $"Lautstärke: {ex.Message}"; ConnectionBanner.IsVisible = true; }
        finally { _volBusy = false; }
    }

    private void Debounced(string key, Action action)
    {
        if (_loading) return;
        if (!_debounce.TryGetValue(key, out var t))
        {
            t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) => { t.Stop(); action(); };
            _debounce[key] = t;
        }
        t.Stop(); t.Start();
    }

    private void SendEqualizer() =>
        Send("headset.eq", ("bands", string.Join(",", _bands.Select(b => Math.Round(b.Value).ToString(CultureInfo.InvariantCulture)))));

    private async void Send(string cmd, params (string, string)[] args)
    {
        try { await _core.CallAsync(cmd, args.ToDictionary(a => a.Item1, a => a.Item2)); }
        catch (Exception ex) { ConnectionBanner.Text = $"Befehl fehlgeschlagen: {ex.Message}"; ConnectionBanner.IsVisible = true; }
    }

    // ---------- polling ----------

    private async Task RefreshAsync()
    {
        JsonElement status;
        int? volume = null;
        try
        {
            status = (await _core.CallAsync("status")).GetProperty("data");
            var vol = (await _core.CallAsync("audio.get")).GetProperty("data").GetProperty("volume");
            if (vol.ValueKind == JsonValueKind.Number) volume = vol.GetInt32();
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            ConnectionBanner.Text = "Der PeriHub-Core läuft nicht. Starte ihn, dann verbindet sich dieses Fenster automatisch.";
            ConnectionBanner.IsVisible = true;
            return;
        }
        catch (Exception ex) { ConnectionBanner.Text = ex.Message; ConnectionBanner.IsVisible = true; return; }

        ConnectionBanner.IsVisible = false;
        _loading = true; // everything below only sets controls from state; must not send commands back
        try
        {
            foreach (var d in status.GetProperty("devices").EnumerateArray())
            {
                var id = d.GetProperty("id").GetString();
                var tile = _tiles.First(t => t.Id == id);
                var connected = d.GetProperty("connected").GetBoolean();
                var battery = d.GetProperty("battery").ValueKind == JsonValueKind.Number ? d.GetProperty("battery").GetInt32() : (int?)null;
                var stateText = d.GetProperty("batteryState").GetString();
                var text = connected ? $"{stateText} · {(battery is { } b ? b + " %" : "Akku unbekannt")}" : stateText;
                tile.Sub.Text = text;
                tile.Pct.Text = connected && battery is { } p ? $"{p} %" : "–";

                switch (id)
                {
                    case "headset":
                        HeadsetStatus.Text = text; HeadsetBattery.Value = battery ?? 0;
                        if (!_presetsFilled && d.GetProperty("info").TryGetProperty("presets", out var presets))
                        {
                            foreach (var name in presets.GetString()!.Split(',')) PresetBox.Items.Add(name);
                            _presetsFilled = true;
                        }
                        break;
                    case "dualsense":
                        PadStatus.Text = text; PadBattery.Value = battery ?? 0;
                        var info = d.GetProperty("info");
                        PadInfo.Text = connected ? $"Verbindung: {info.GetProperty("connection").GetString()}   ·   Firmware: {info.GetProperty("firmware").GetString()}" : "";
                        break;
                }
            }
            if (volume is { } v && !_volBusy && !VolumeSlider.IsPointerOver)
            {
                VolumeSlider.Value = v; VolumeLabel.Text = $"{v} %";
            }
        }
        finally { _loading = false; }
    }
}
