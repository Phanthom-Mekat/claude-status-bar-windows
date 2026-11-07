# Minimal StatusNotifierWatcher: stands in for the GNOME/KDE panel so the app registers its tray item.
from gi.repository import Gio, GLib
XML = """<node><interface name="org.kde.StatusNotifierWatcher">
<method name="RegisterStatusNotifierItem"><arg type="s" direction="in"/></method>
<method name="RegisterStatusNotifierHost"><arg type="s" direction="in"/></method>
<property name="RegisteredStatusNotifierItems" type="as" access="read"/>
<property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
<property name="ProtocolVersion" type="i" access="read"/>
<signal name="StatusNotifierItemRegistered"><arg type="s"/></signal>
<signal name="StatusNotifierItemUnregistered"><arg type="s"/></signal>
<signal name="StatusNotifierHostRegistered"/>
</interface></node>"""
items = []
def call(conn, sender, path, iface, method, params, inv):
    if method == "RegisterStatusNotifierItem":
        s = params.unpack()[0]
        items.append(s)
        print("REGISTERED", sender, s, flush=True)
        with open("/tmp/sni.txt", "w") as f: f.write(f"{sender} {s}\n")
    inv.return_value(None)
def getprop(conn, sender, path, iface, prop):
    return {"RegisteredStatusNotifierItems": GLib.Variant("as", items),
            "IsStatusNotifierHostRegistered": GLib.Variant("b", True),
            "ProtocolVersion": GLib.Variant("i", 0)}[prop]
def on_bus(conn, name):
    conn.register_object("/StatusNotifierWatcher", Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call, getprop, None)
Gio.bus_own_name(Gio.BusType.SESSION, "org.kde.StatusNotifierWatcher", 0, on_bus, None, None)
GLib.MainLoop().run()
