using System.Diagnostics;
using AppStatServer.Data;

namespace AppStatServer;

// Host resource probes (disk, RAM, this process's memory) for the Maintenance page.
// Everything is best-effort: a probe that can't read its source returns zeros rather than
// throwing, so a partial reading is always better than a failed page.
public static class SystemMetrics
{
    // Free/total space on the drive that holds dataPath (falls back to the app's own drive).
    public static DiskInfo GetDisk(string? dataPath)
    {
        var info = new DiskInfo();
        try
        {
            var probe = string.IsNullOrEmpty(dataPath) ? AppContext.BaseDirectory : dataPath;
            var root = Path.GetPathRoot(Path.GetFullPath(probe));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                info.Drive = drive.Name;
                info.TotalBytes = drive.TotalSize;
                info.FreeBytes = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Drive may be a network/virtual path we can't stat — leave the zeros.
        }

        return info;
    }

    // System RAM (honouring container limits) plus this process's footprint. The machine-wide
    // figures come from the GC's view of memory, which is cross-platform and cgroup-aware.
    public static MemoryInfo GetMemory()
    {
        var m = new MemoryInfo();

        var gc = GC.GetGCMemoryInfo();
        m.TotalBytes = gc.TotalAvailableMemoryBytes;
        m.UsedBytes = gc.MemoryLoadBytes;

        using var proc = Process.GetCurrentProcess();
        m.ProcessWorkingSetBytes = proc.WorkingSet64;
        m.ProcessManagedHeapBytes = GC.GetTotalMemory(false);

        return m;
    }
}
