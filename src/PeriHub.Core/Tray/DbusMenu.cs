using Tmds.DBus.Protocol;

namespace PeriHub.Core.Tray;

/// <summary>Right-click menu for the tray icon, speaking the com.canonical.dbusmenu protocol.</summary>
public sealed class DbusMenu(DBusConnection conn, Action onOpen) : IPathMethodHandler
{
    private const string Iface = "com.canonical.dbusmenu";
    private sealed record Item(int Id, string Label, bool Enabled, bool Separator);

    private List<Item> _items = Build(null, null);
    private uint _revision = 1;

    public string Path => "/MenuBar";
    public bool HandlesChildPaths => false;

    private static List<Item> Build(string? headset, string? pad)
    {
        var items = new List<Item> { new(1, "PeriHub öffnen", true, false) };
        if (headset is null && pad is null) return items;
        items.Add(new(2, "", false, true));
        if (headset is not null) items.Add(new(3, headset, false, false)); // disabled = shown as info text, not clickable
        if (pad is not null) items.Add(new(4, pad, false, false));
        return items;
    }

    /// <summary>Pass null for a device that is off or has no battery value: its line is then left out.</summary>
    public void SetBatteryLines(string? headset, string? pad)
    {
        var next = Build(headset, pad);
        if (next.SequenceEqual(_items)) return;
        _items = next;
        _revision++;
        using var w = conn.GetMessageWriter();
        w.WriteSignalHeader(null, Path, Iface, "LayoutUpdated", "ui");
        w.WriteUInt32(_revision);
        w.WriteInt32(0);
        conn.TrySendMessage(w.CreateMessage());
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var req = context.Request;
        try
        {
            switch (req.InterfaceAsString, req.MemberAsString)
            {
                case (Iface, "GetLayout"):
                {
                    var w = context.CreateReplyWriter("u(ia{sv}av)");
                    w.WriteUInt32(_revision);
                    WriteNode(ref w, 0, null, _items);
                    context.Reply(w.CreateMessage()); w.Dispose();
                    break;
                }
                case (Iface, "GetGroupProperties"):
                {
                    var r = req.GetBodyReader();
                    var ids = r.ReadArrayOfInt32();
                    var w = context.CreateReplyWriter("a(ia{sv})");
                    var arr = w.WriteArrayStart(DBusType.Struct);
                    var wanted = ids.Length == 0 ? _items.Select(i => i.Id).Prepend(0).ToArray() : ids;
                    foreach (var id in wanted)
                    {
                        w.WriteStructureStart();
                        w.WriteInt32(id);
                        WriteProps(ref w, id == 0 ? null : _items.FirstOrDefault(i => i.Id == id));
                    }
                    w.WriteArrayEnd(arr);
                    context.Reply(w.CreateMessage()); w.Dispose();
                    break;
                }
                case (Iface, "AboutToShow"):
                {
                    var w = context.CreateReplyWriter("b");
                    w.WriteBool(false); // layout is always current
                    context.Reply(w.CreateMessage()); w.Dispose();
                    break;
                }
                case (Iface, "Event"):
                {
                    var r = req.GetBodyReader();
                    var id = r.ReadInt32(); var ev = r.ReadString();
                    if (ev == "clicked" && id == 1) onOpen();
                    ReplyEmpty(context);
                    break;
                }
                case ("org.freedesktop.DBus.Properties", "Get"):
                {
                    var r = req.GetBodyReader(); r.ReadString(); var name = r.ReadString();
                    var w = context.CreateReplyWriter("v");
                    if (!WriteProperty(ref w, name)) { w.Dispose(); context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name); break; }
                    context.Reply(w.CreateMessage()); w.Dispose();
                    break;
                }
                case ("org.freedesktop.DBus.Properties", "GetAll"):
                {
                    var w = context.CreateReplyWriter("a{sv}");
                    var arr = w.WriteDictionaryStart();
                    foreach (var n in new[] { "Version", "TextDirection", "Status", "IconThemePaths" })
                    {
                        w.WriteDictionaryEntryStart(); w.WriteString(n); WriteProperty(ref w, n);
                    }
                    w.WriteDictionaryEnd(arr);
                    context.Reply(w.CreateMessage()); w.Dispose();
                    break;
                }
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"dbusmenu: {ex.Message}"); context.ReplyError(null, ex.Message); }
        return default;
    }

    private static void ReplyEmpty(MethodContext c) { var w = c.CreateReplyWriter(null); c.Reply(w.CreateMessage()); w.Dispose(); }

    private static bool WriteProperty(ref MessageWriter w, string name)
    {
        switch (name)
        {
            case "Version": w.WriteVariantUInt32(3); return true;
            case "TextDirection": w.WriteVariantString("ltr"); return true;
            case "Status": w.WriteVariantString("normal"); return true;
            case "IconThemePaths":
                w.WriteSignature("as");
                var a = w.WriteArrayStart(DBusType.String); w.WriteArrayEnd(a);
                return true;
            default: return false;
        }
    }

    /// <summary>Writes one (ia{sv}av) node: id, properties, children (each child wrapped in a variant).</summary>
    private static void WriteNode(ref MessageWriter w, int id, Item? item, IReadOnlyList<Item> children)
    {
        w.WriteStructureStart();
        w.WriteInt32(id);
        WriteProps(ref w, item);
        var arr = w.WriteArrayStart(DBusType.Variant);
        foreach (var c in children)
        {
            w.WriteSignature("(ia{sv}av)");
            WriteNode(ref w, c.Id, c, []);
        }
        w.WriteArrayEnd(arr);
    }

    private static void WriteProps(ref MessageWriter w, Item? item)
    {
        var d = w.WriteDictionaryStart();
        if (item is null)
        {
            w.WriteDictionaryEntryStart(); w.WriteString("children-display"); w.WriteVariantString("submenu");
        }
        else if (item.Separator)
        {
            w.WriteDictionaryEntryStart(); w.WriteString("type"); w.WriteVariantString("separator");
        }
        else
        {
            w.WriteDictionaryEntryStart(); w.WriteString("label"); w.WriteVariantString(item.Label);
            w.WriteDictionaryEntryStart(); w.WriteString("enabled"); w.WriteVariantBool(item.Enabled);
        }
        w.WriteDictionaryEnd(d);
    }
}
