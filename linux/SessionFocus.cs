using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeStatusBar;

/// <summary>Jump to a session (Linux port of the Windows SessionFocus): bring the window hosting its claude process to
/// the front, and for the Claude Code extension (VS Code, Cursor, Windsurf) open that exact conversation tab via the
/// extension's <c>&lt;editor&gt;://anthropic.claude-code/open?session=&lt;id&gt;</c> link.
/// Windows are found on X11 through the window manager's client list (_NET_WM_PID) and raised with a pager-style
/// _NET_ACTIVE_WINDOW request, which window managers honor despite focus-stealing prevention. Under Wayland only
/// XWayland windows are visible this way (VS Code is one by default; GNOME Terminal isn't) — Wayland lets no app
/// raise another's window.</summary>
public static class SessionFocus
{
    // Ancestors that are never the session's host: walking past them would land on the session/display manager.
    static readonly HashSet<string> Stop = new() { "systemd", "init", "gnome-shell", "kwin_x11", "kwin_wayland", "plasmashell", "sddm", "gdm", "lightdm", "login", "sshd" };

    // Folder names too generic to identify a window by ("src" appears in plenty of titles).
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        { "src", "app", "apps", "lib", "libs", "test", "tests", "docs", "packages", "source", "code", "projects", "repos", "users", "home" };

    public static bool Jump(int claudePid, string cwd, string sessionId, string entrypoint)
    {
        var ancestors = claudePid > 0 ? ProcessTree.Ancestors(claudePid) : new();
        bool focused = X11.Activate(ancestors, cwd);
        if (entrypoint == "claude-vscode" && Guid.TryParse(sessionId, out _))
        {
            var url = $"{EditorScheme(ancestors.Select(a => a.name))}://anthropic.claude-code/open?session={sessionId}";
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { url }, UseShellExecute = false })?.Dispose();
            return true;
        }
        return focused;
    }

    /// <summary>Pure: the URL scheme of the editor hosting the extension, from the session's ancestor process names.</summary>
    public static string EditorScheme(IEnumerable<string> ancestorNames)
    {
        foreach (var n in ancestorNames)
            switch (n.ToLowerInvariant())
            {
                case "code-insiders": return "vscode-insiders";
                case "cursor": return "cursor";
                case "windsurf": return "windsurf";
                case "code": return "vscode";
            }
        return "vscode";
    }

    /// <summary>Pure: the nearest ancestor that owns windows is the host. Among its windows (topmost first), prefer the
    /// one whose title names the session's folder or a parent folder — editors title windows by workspace root, and
    /// terminals like GNOME Terminal serve every window from one process. Deepest folder wins.</summary>
    public static ulong Pick(IReadOnlyList<(int pid, string name)> ancestors,
                             IReadOnlyDictionary<int, List<(ulong win, string title)>> windowsByPid, string cwd)
    {
        var folders = cwd.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries)
            .Reverse().Where(f => f.Length >= 3 && !Generic.Contains(f)).ToList();
        foreach (var (pid, name) in ancestors)
        {
            if (Stop.Contains(name)) break;
            if (!windowsByPid.TryGetValue(pid, out var wins) || wins.Count == 0) continue;
            foreach (var folder in folders)
                foreach (var w in wins)
                    if (w.title.Contains(folder, StringComparison.OrdinalIgnoreCase)) return w.win;
            return wins[0].win;
        }
        return 0;
    }

    static class X11
    {
        public static bool Activate(IReadOnlyList<(int pid, string name)> ancestors, string cwd)
        {
            if (ancestors.Count == 0) return false;
            IntPtr d;
            try { d = XOpenDisplay(null); } catch { return false; } // no libX11 (pure Wayland session without XWayland)
            if (d == IntPtr.Zero) return false;
            try
            {
                var root = XDefaultRootWindow(d);
                var win = Pick(ancestors, Windows(d, root), cwd);
                if (win == 0) return false;
                SendActivate(d, root, win);
                return true;
            }
            catch (Exception e) { Log.Write("jump: x11: " + e.Message); return false; }
            finally { XCloseDisplay(d); }
        }

        /// <summary>Managed top-level windows grouped by owning pid, topmost first.</summary>
        static Dictionary<int, List<(ulong, string)>> Windows(IntPtr d, ulong root)
        {
            var map = new Dictionary<int, List<(ulong, string)>>();
            var list = Longs(d, root, Atom(d, "_NET_CLIENT_LIST_STACKING"));
            if (list.Length == 0) list = Longs(d, root, Atom(d, "_NET_CLIENT_LIST"));
            ulong pidAtom = Atom(d, "_NET_WM_PID"), nameAtom = Atom(d, "_NET_WM_NAME"), wmName = Atom(d, "WM_NAME");
            for (int i = list.Length - 1; i >= 0; i--) // stacking order is bottom→top
            {
                var w = list[i];
                var pids = Longs(d, w, pidAtom);
                if (pids.Length == 0) continue;
                var title = Text(d, w, nameAtom);
                if (title.Length == 0) title = Text(d, w, wmName);
                if (!map.TryGetValue((int)pids[0], out var l)) map[(int)pids[0]] = l = new();
                l.Add((w, title));
            }
            return map;
        }

        static void SendActivate(IntPtr d, ulong root, ulong win)
        {
            // XClientMessageEvent on LP64: type@0, serial@8, send_event@16, display@24, window@32, message_type@40,
            // format@48, data.l[5]@56. The XEvent union is 24 longs.
            var ev = Marshal.AllocHGlobal(192);
            try
            {
                for (int i = 0; i < 192; i += 8) Marshal.WriteInt64(ev, i, 0);
                Marshal.WriteInt32(ev, 0, 33);                                   // ClientMessage
                Marshal.WriteInt32(ev, 16, 1);                                   // send_event
                Marshal.WriteIntPtr(ev, 24, d);
                Marshal.WriteInt64(ev, 32, (long)win);
                Marshal.WriteInt64(ev, 40, (long)Atom(d, "_NET_ACTIVE_WINDOW"));
                Marshal.WriteInt32(ev, 48, 32);
                Marshal.WriteInt64(ev, 56, 2);                                   // source: pager (user-requested)
                const long SubstructureNotify = 1 << 19, SubstructureRedirect = 1 << 20;
                XSendEvent(d, root, false, SubstructureNotify | SubstructureRedirect, ev);
                XFlush(d);
            }
            finally { Marshal.FreeHGlobal(ev); }
        }

        static ulong Atom(IntPtr d, string name) => XInternAtom(d, name, false);

        // Format-32 properties come back as C longs (8 bytes on LP64).
        static ulong[] Longs(IntPtr d, ulong w, ulong prop)
        {
            if (XGetWindowProperty(d, w, prop, 0, 4096, false, 0, out _, out int fmt, out ulong n, out _, out var data) != 0 || data == IntPtr.Zero)
                return Array.Empty<ulong>();
            try
            {
                if (fmt != 32) return Array.Empty<ulong>();
                var r = new ulong[n];
                for (ulong i = 0; i < n; i++) r[i] = (ulong)Marshal.ReadInt64(data, (int)(i * 8));
                return r;
            }
            finally { XFree(data); }
        }

        static string Text(IntPtr d, ulong w, ulong prop)
        {
            if (XGetWindowProperty(d, w, prop, 0, 1024, false, 0, out _, out int fmt, out ulong n, out _, out var data) != 0 || data == IntPtr.Zero)
                return "";
            try
            {
                if (fmt != 8 || n == 0) return "";
                var bytes = new byte[n];
                Marshal.Copy(data, bytes, 0, (int)n);
                return Encoding.UTF8.GetString(bytes);
            }
            finally { XFree(data); }
        }

        const string Lib = "libX11.so.6";
        [DllImport(Lib)] static extern IntPtr XOpenDisplay(string? name);
        [DllImport(Lib)] static extern int XCloseDisplay(IntPtr d);
        [DllImport(Lib)] static extern ulong XDefaultRootWindow(IntPtr d);
        [DllImport(Lib)] static extern ulong XInternAtom(IntPtr d, string name, bool onlyIfExists);
        [DllImport(Lib)] static extern int XGetWindowProperty(IntPtr d, ulong w, ulong prop, long offset, long length, bool delete,
            ulong reqType, out ulong actualType, out int actualFormat, out ulong nItems, out ulong bytesAfter, out IntPtr data);
        [DllImport(Lib)] static extern int XFree(IntPtr data);
        [DllImport(Lib)] static extern int XSendEvent(IntPtr d, ulong w, bool propagate, long mask, IntPtr ev);
        [DllImport(Lib)] static extern int XFlush(IntPtr d);
    }
}
