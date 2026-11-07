# Fake org.freedesktop.portal.Desktop with just the Inhibit interface: logs Inhibit/Close to /tmp/portal.log.
from gi.repository import Gio, GLib
INHIBIT = """<node><interface name="org.freedesktop.portal.Inhibit">
<method name="Inhibit"><arg type="s" direction="in"/><arg type="u" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
</interface></node>"""
REQUEST = """<node><interface name="org.freedesktop.portal.Request"><method name="Close"/></interface></node>"""
req_info = Gio.DBusNodeInfo.new_for_xml(REQUEST).interfaces[0]
n = [0]
def log(s):
    with open("/tmp/portal.log", "a") as f: f.write(s + "\n")
def call(conn, sender, path, iface, method, params, inv):
    if method == "Inhibit":
        _, flags, opts = params.unpack()
        n[0] += 1
        p = f"/org/freedesktop/portal/desktop/request/t/{n[0]}"
        conn.register_object(p, req_info, call, None, None)
        log(f"INHIBIT flags={flags} reason={opts.get('reason')} -> {p}")
        inv.return_value(GLib.Variant("(o)", (p,)))
    elif method == "Close":
        log(f"CLOSE {path}")
        inv.return_value(None)
def on_bus(conn, name):
    conn.register_object("/org/freedesktop/portal/desktop", Gio.DBusNodeInfo.new_for_xml(INHIBIT).interfaces[0], call, None, None)
Gio.bus_own_name(Gio.BusType.SESSION, "org.freedesktop.portal.Desktop", 0, on_bus, None, None)
GLib.MainLoop().run()
