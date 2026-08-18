using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DshWebLauncher.Services;

internal sealed record ProcessTreeMetrics(int ServiceProcessId, long WorkingSetBytes, DateTime StartTime);

internal static class ProcessTreeService
{
    private const uint SnapshotProcesses = 0x00000002;

    public static ProcessTreeMetrics GetMetrics(Process root)
    {
        var parentMap = EnumerateParents();
        var ids = new HashSet<int> { root.Id };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var pair in parentMap)
            {
                if (ids.Contains(pair.Value) && ids.Add(pair.Key)) added = true;
            }
        }

        var processes = new List<Process>();
        foreach (var id in ids)
        {
            try { processes.Add(Process.GetProcessById(id)); } catch (ArgumentException) { }
        }
        try
        {
            var service = processes.FirstOrDefault(process => process.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase)) ?? root;
            var workingSet = processes.Sum(process => { try { return process.WorkingSet64; } catch (InvalidOperationException) { return 0L; } });
            var startTime = processes.Select(process => { try { return process.StartTime; } catch (InvalidOperationException) { return DateTime.Now; } }).Min();
            return new ProcessTreeMetrics(service.Id, workingSet, startTime);
        }
        finally
        {
            foreach (var process in processes) if (!ReferenceEquals(process, root)) process.Dispose();
        }
    }

    private static Dictionary<int, int> EnumerateParents()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do { result[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Process32Next(snapshot, ref entry));
            return result;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
