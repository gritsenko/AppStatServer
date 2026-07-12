namespace AppStatServer.Data;

// A live snapshot of the host's resources for the Maintenance page: free disk and RAM,
// what this process is consuming, and how much storage our own data (the LiteDB file)
// takes, broken down by collection.
public class SystemInfo
{
    public DiskInfo Disk { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public StorageInfo Storage { get; set; } = new();
}

// Free/total space on the drive the database lives on.
public class DiskInfo
{
    public string Drive { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;
}

public class MemoryInfo
{
    // System RAM available to the runtime (machine RAM, or the container/cgroup limit when
    // running under one) and how much of it is currently in use across the machine.
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes => Math.Max(0, TotalBytes - UsedBytes);

    // This process only: OS-reported working set and the managed heap it holds.
    public long ProcessWorkingSetBytes { get; set; }
    public long ProcessManagedHeapBytes { get; set; }
}

// Storage consumed by our own data. The whole app persists into a single LiteDB file, so
// the file size is the storage on disk; the per-collection logical sizes add up to less
// (the file also carries indexes, page headers, and free pages).
public class StorageInfo
{
    public string DatabasePath { get; set; } = string.Empty;
    public long DatabaseFileBytes { get; set; }
    public long DataBytes { get; set; }
    public List<CollectionInfo> Collections { get; set; } = [];
}

public class CollectionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long Documents { get; set; }
    public long Bytes { get; set; }
}
