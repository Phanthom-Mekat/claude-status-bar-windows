using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

/// <summary>Given the hostPid a session recorded at SessionStart (the Claude Code CLI process), walks up
/// the LIVE process tree to find the nearest ancestor that owns a visible top-level window — the terminal
/// or editor hosting that session — and brings it to the foreground. Windows-only, best-effort: any
/// failure (dead pid, no matching window, OS denies the foreground switch) just returns false.</summary>
public static class WindowFocus
{
    public static bool FocusSessionWindow(int hostPid)
    {
        if (hostPid <= 0) return false;
        var hwnd = FindWindowForPidChain(hostPid);
        if (hwnd == IntPtr.Zero) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        return SetForegroundWindow(hwnd);
    }

    // hostPid itself is rarely the window owner (it's the CLI process); walk its live ancestors — shell,
    // then terminal emulator / VS Code — until one owns a visible top-level window. Capped depth guards
    // against a parent-pid cycle from pid reuse.
    static IntPtr FindWindowForPidChain(int startPid)
    {
        var parents = LiveParentMap();
        var seen = new HashSet<int>();
        int pid = startPid;
        for (int i = 0; i < 16 && pid > 0 && seen.Add(pid); i++)
        {
            var hwnd = TopWindowForPid(pid);
            if (hwnd != IntPtr.Zero) return hwnd;
            if (!parents.TryGetValue(pid, out var parent)) break;
            pid = parent;
        }
        return IntPtr.Zero;
    }

    static Dictionary<int, int> LiveParentMap()
    {
        var map = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap.ToInt64() == -1) return map;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snap, ref entry))
                do { map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID; }
                while (Process32Next(snap, ref entry));
        }
        finally { CloseHandle(snap); }
        return map;
    }

    static IntPtr TopWindowForPid(int pid)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != (uint)pid) return true;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true; // skip owned popups
            if (GetWindowTextLength(hwnd) == 0) return true;           // skip titleless helper windows
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    const uint GW_OWNER = 4;
    const int SW_RESTORE = 9;
    const uint TH32CS_SNAPPROCESS = 0x2;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int priClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll")] static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
}
