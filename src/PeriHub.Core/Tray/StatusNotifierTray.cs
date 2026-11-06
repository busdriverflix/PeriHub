using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace PeriHub.Core.Tray;

/// <summary>Minimal StatusNotifierItem (SNI) tray icon, talking D-Bus directly. Works on KDE, Cinnamon, XFCE, GNOME+AppIndicator.</summary>
public sealed class StatusNotifierTray : IPathMethodHandler, IDisposable
{
    private const string ItemPath = "/StatusNotifierItem";
    private const string Iface = "org.kde.StatusNotifierItem";
    private readonly string _service = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
    private DBusConnection? _conn;
    private byte[] _icon = IconRenderer.Render(null);
    private string _tooltip = "PeriHub";

    public string Path => ItemPath;
    public bool HandlesChildPaths => false;
    public Action? OnActivate { get; set; }

    public async Task StartAsync(CancellationToken ct)
    {
        _conn = new DBusConnection(DBusAddress.Session!);
        await _conn.ConnectAsync();
        _conn.AddMethodHandler(this);
        _ = _conn.DisconnectedAsync().ContinueWith(t => Console.Error.WriteLine($"D-Bus disconnected: {t.Result}"));
        var got = await _conn.TryRequestNameAsync(_service, RequestNameOptions.Default);
        Console.WriteLine($"Bus name {_service}: {(got ? "acquired" : "NOT acquired")} (unique {_conn.UniqueName})");
        if (Environment.GetEnvironmentVariable("PERIHUB_NOREG") != "1") _ = Task.Run(() => RegisterLoopAsync(ct), ct);
    }

    /// <summary>The watcher may not exist yet right after login, so keep trying until the panel is up.</summary>
    private async Task RegisterLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _conn!.CallMethodAsync(BuildRegisterMessage());
                Console.WriteLine("Tray icon registered");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Tray register failed ({ex.Message}), retrying in 10s");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }

    private MessageBuffer BuildRegisterMessage()
    {
        using var w = _conn!.GetMessageWriter();
        w.WriteMethodCallHeader("org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher",
            "org.kde.StatusNotifierWatcher", "RegisterStatusNotifierItem", "s", MessageFlags.None);
        w.WriteString(_service);
        return w.CreateMessage();
    }

    public void Update(int? percent, string tooltip)
    {
        _icon = IconRenderer.Render(percent);
        _tooltip = tooltip;
        Signal("NewIcon");
        Signal("NewToolTip");
    }

    private void Signal(string member)
    {
        if (_conn is null) return;
        using var w = _conn.GetMessageWriter();
        w.WriteSignalHeader(null, ItemPath, Iface, member, null);
        _conn.TrySendMessage(w.CreateMessage());
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var req = context.Request;
        switch (req.InterfaceAsString, req.MemberAsString)
        {
            case ("org.freedesktop.DBus.Properties", "Get"):
            {
                var r = req.GetBodyReader();
                r.ReadString(); var name = r.ReadString();
                var w = context.CreateReplyWriter("v");
                if (!WriteVariant(ref w, name)) { w.Dispose(); context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name); return default; }
                context.Reply(w.CreateMessage());
                w.Dispose();
                return default;
            }
            case ("org.freedesktop.DBus.Properties", "GetAll"):
            {
                var w = context.CreateReplyWriter("a{sv}");
                var arr = w.WriteDictionaryStart();
                foreach (var n in PropertyNames)
                {
                    w.WriteDictionaryEntryStart();
                    w.WriteString(n);
                    WriteVariant(ref w, n);
                }
                w.WriteDictionaryEnd(arr);
                context.Reply(w.CreateMessage());
                w.Dispose();
                return default;
            }
            case (Iface, "Activate"):
                try { OnActivate?.Invoke(); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
                ReplyEmpty(context); return default;
            case (Iface, "ContextMenu" or "SecondaryActivate" or "Scroll"):
                ReplyEmpty(context); return default;
            default:
                context.ReplyUnknownMethodError(); return default;
        }
    }

    private static void ReplyEmpty(MethodContext c) { var w = c.CreateReplyWriter(null); c.Reply(w.CreateMessage()); w.Dispose(); }

    private static readonly string[] PropertyNames =
        ["Category", "Id", "Title", "Status", "WindowId", "IconName", "IconPixmap", "OverlayIconName", "AttentionIconName", "ToolTip", "ItemIsMenu", "Menu"];

    private bool WriteVariant(ref MessageWriter w, string name)
    {
        switch (name)
        {
            case "Category": w.WriteVariantString("Hardware"); break;
            case "Id": w.WriteVariantString("perihub"); break;
            case "Title": w.WriteVariantString("PeriHub"); break;
            case "Status": w.WriteVariantString("Active"); break;
            case "WindowId": w.WriteVariantUInt32(0); break;
            case "IconName" or "OverlayIconName" or "AttentionIconName": w.WriteVariantString(""); break;
            case "ItemIsMenu": w.WriteVariantBool(false); break;
            case "Menu": w.WriteVariantObjectPath("/NO_DBUSMENU"); break;
            case "IconPixmap":
            {
                w.WriteSignature("a(iiay)");
                var arr = w.WriteArrayStart(DBusType.Struct);
                w.WriteStructureStart();
                w.WriteInt32(IconRenderer.Size); w.WriteInt32(IconRenderer.Size);
                w.WriteArray(_icon);
                w.WriteArrayEnd(arr);
                break;
            }
            case "ToolTip":
            {
                w.WriteSignature("(sa(iiay)ss)");
                w.WriteStructureStart();
                w.WriteString("");
                var arr = w.WriteArrayStart(DBusType.Struct);
                w.WriteArrayEnd(arr);
                w.WriteString("PeriHub");
                w.WriteString(_tooltip);
                break;
            }
            default: return false;
        }
        return true;
    }

    public static void LaunchGui()
    {
        var exe = Environment.GetEnvironmentVariable("PERIHUB_GUI");
        if (exe is null)
        {
            var sibling = System.IO.Path.Combine(AppContext.BaseDirectory, "PeriHub.Gui");
            var dev = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../PeriHub.Gui/bin/Debug/net10.0/PeriHub.Gui"));
            exe = File.Exists(sibling) ? sibling : dev; // dev fallback: sibling project's debug build
        }
        if (!File.Exists(exe)) { Console.Error.WriteLine($"GUI not found: {exe}"); return; }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
    }

    public void Dispose() => _conn?.Dispose();
}
