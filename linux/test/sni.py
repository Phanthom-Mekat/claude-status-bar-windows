# Reads the tray item like a panel would: dump icon -> PNG, tooltip, full DBus menu; optionally click a menu item.
import sys, subprocess
from gi.repository import Gio, GLib
bus = Gio.bus_get_sync(Gio.BusType.SESSION)
sender, name = open("/tmp/sni.txt").read().split()
svc, path = (name, "/StatusNotifierItem") if not name.startswith("/") else (sender, name)
def prop(p):
    return bus.call_sync(svc, path, "org.freedesktop.DBus.Properties", "Get",
        GLib.Variant("(ss)", ("org.kde.StatusNotifierItem", p)), GLib.VariantType("(v)"), 0, -1, None).unpack()[0]
menu_path = prop("Menu")
def layout():
    return bus.call_sync(svc, menu_path, "com.canonical.dbusmenu", "GetLayout",
        GLib.Variant("(iias)", (0, -1, [])), None, 0, -1, None).unpack()[1]
def walk(node, depth, out):
    nid, props, kids = node
    if depth >= 0:
        if props.get("type") == "separator": out.append("  " * depth + "----")
        else:
            label = props.get("label", "")
            tt, ts = props.get("toggle-type", ""), props.get("toggle-state", -1)
            mark = {"checkmark": "[x] " if ts == 1 else "[ ] ", "radio": "(o) " if ts == 1 else "( ) "}.get(tt, "")
            flags = (" (disabled)" if props.get("enabled", True) is False else "") + (" [icon]" if "icon-data" in props else "")
            out.append(f"{'  ' * depth}{mark}{label}{flags}   #{nid}")
    for k in kids: walk(k, depth + 1, out)
def walk_nodes(node):
    yield node
    for k in node[2]: yield from walk_nodes(k)
def find(node, label):
    nid, props, kids = node
    if props.get("label", "").startswith(label): return nid
    for k in kids:
        r = find(k, label)
        if r is not None: return r
cmd = sys.argv[1]
if cmd == "dump":
    pix = prop("IconPixmap")
    w, h, data = max(pix, key=lambda t: t[0])
    raw = bytes(data); rgba = bytearray()
    for i in range(0, len(raw), 4): rgba += bytes([raw[i+1], raw[i+2], raw[i+3], raw[i]])
    open("/tmp/icon.rgba", "wb").write(rgba)
    subprocess.run(["convert", "-size", f"{w}x{h}", "-depth", "8", "rgba:/tmp/icon.rgba", "-scale", "400%", sys.argv[2]])
    print("tooltip:", prop("ToolTip")[2], "| icon", w, "x", h)
    out = []; walk(layout(), -1, out); print("\n".join(out))
elif cmd == "tooltip":
    print("ToolTip raw:", prop("ToolTip"), "| Title:", prop("Title"))
elif cmd == "click":
    # "Parent>Child" clicks Child inside the submenu whose label starts with Parent. The app renumbers the menu
    # whenever it changes (live timers: every second), so a click can race a renumber: EventGroup reports ids it
    # didn't know, and we retry with a fresh layout.
    import time
    for attempt in range(5):
        node = layout()
        *parents, leaf = sys.argv[2].split(">")
        for par in parents:
            pid_ = find(node, par)
            node = next(k for k in walk_nodes(node) if k[0] == pid_)
        nid = find(node, leaf)
        if nid is None: print("NOT FOUND", sys.argv[2]); sys.exit(1)
        errs = bus.call_sync(svc, menu_path, "com.canonical.dbusmenu", "EventGroup",
            GLib.Variant("(a(isvu))", ([(nid, "clicked", GLib.Variant("i", 0), 0)],)), None, 0, -1, None).unpack()[0]
        if not errs: print("clicked", sys.argv[2], "#", nid); break
        time.sleep(0.2)
    else:
        print("STALE", sys.argv[2]); sys.exit(1)
