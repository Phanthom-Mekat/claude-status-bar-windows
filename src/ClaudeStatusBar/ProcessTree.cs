using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeStatusBar;

/// <summary>Maps a hook to its session's long-lived <c>claude.exe</c>. On Windows Claude Code runs hooks
/// through Git Bash, so a hook's parent is a short-lived bash.exe (claude.exe → bash → bash → node), not
/// claude itself as on macOS. The hook calls <c>ClaudeStatusBar --claude-pid &lt;its own pid&gt;</c> once
/// per session while its chain is still alive; we walk a Toolhelp snapshot (instant, unlike WMI's ~1 s).</summary>
public static class ProcessTree
{
    /// <summary>Nearest ancestor (or self) named claude.exe, or -1.</summary>
    public static int FindClaudeAncestor(int pid)
    {
        var map = Snapshot();
        for (int i = 0; i < 16 && pid > 0 && map.TryGetValue(pid, out var e); i++)
        {
            if (e.name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)) return pid;
            if (e.parent == pid) break;
            pid = e.parent;
        }
        return -1;
    }

    /// <summary>The pid and its ancestors, nearest first, with exe names (for finding a session's window).</summary>
    public static List<(int pid, string name)> Ancestors(int pid)
    {
        var map = Snapshot();
        var list = new List<(int, string)>();
        for (int i = 0; i < 16 && pid > 0 && map.TryGetValue(pid, out var e); i++)
        {
            list.Add((pid, e.name));
            if (e.parent == pid) break;
            pid = e.parent;
        }
        return list;
    }

    /// <summary>Liveness: the pid still exists AND is still a claude process (guards pid reuse).</summary>
    public static bool IsClaudeAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; } // ArgumentException = gone
    }

    static Dictionary<int, (int parent, string name)> Snapshot()
    {
        var map = new Dictionary<int, (int, string)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
        try
        {
            var pe = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (bool ok = Process32FirstW(snap, ref pe); ok; ok = Process32NextW(snap, ref pe))
                map[(int)pe.th32ProcessID] = ((int)pe.th32ParentProcessID, pe.szExeFile);
        }
        finally { CloseHandle(snap); }
        return map;
    }

    const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W pe);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W pe);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
