namespace AppStatServer.Data;

// AppCenter-style diagnostics: crashes and handled errors side by side, over a rolling
// window of days, optionally narrowed to a single app version (release).
public class DiagnosticsReport
{
    public int Days { get; set; }
    public string? Release { get; set; }

    public int TotalCrashes { get; set; }
    public int TotalErrors { get; set; }
    public int AffectedUsers { get; set; }   // distinct users hit by a crash or error in the window
    public int OpenGroups { get; set; }       // unresolved crash/error signatures

    public List<DailyCount> CrashesPerDay { get; set; } = [];
    public List<DailyCount> ErrorsPerDay { get; set; } = [];

    public List<DiagnosticGroup> Groups { get; set; } = []; // both kinds, newest activity first
}

// One collapsed crash or error signature. Kind is "crash" (unhandled, app terminated) or
// "error" (handled exception the app reported and kept running from).
public class DiagnosticGroup
{
    public string Key { get; set; } = string.Empty;   // stable id used to mark it resolved
    public string Kind { get; set; } = "error";
    public string Title { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;

    public int Count { get; set; }
    public int Users { get; set; }
    public string? Release { get; set; }

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public AppEvent Sample { get; set; } = new(); // latest occurrence (carries the stack trace)
}

// Persisted resolution state, keyed by DiagnosticGroup.Key. A group counts as resolved while
// its latest occurrence is not newer than ResolvedAt — so a resolved issue reopens if it recurs.
public class Resolution
{
    public string Key { get; set; } = string.Empty;
    public DateTime ResolvedAt { get; set; }
}

// Posted to /api/resolve to mark a crash/error group resolved (or reopen it).
public record ResolveRequest(string Key, bool Resolved);
