namespace AppStatServer.Data;

// Aggregated numbers that power the dashboard's stat cards and charts.
public class DashboardStats
{
    public int TotalEvents { get; set; }
    public int Errors { get; set; }
    public int Crashes { get; set; }
    public int TotalSessions { get; set; }

    // Custom product (track) events — a separate signal from the Sentry TotalEvents above.
    public int CustomEvents { get; set; }

    // Rolling-window health signals for the overview page. "Last 7 days" includes today;
    // the preceding 7 days give the baseline for trend deltas.
    public int EventsToday { get; set; }
    public int ErrorsLast7Days { get; set; }
    public int ErrorsPrev7Days { get; set; }
    public int CrashesLast7Days { get; set; }
    public int CrashesPrev7Days { get; set; }
    public int SessionsLast7Days { get; set; }

    // Share of last-7-days sessions that recorded zero errors (a crash-free-sessions proxy,
    // since sessions carry an error count rather than a crashed flag). Null when no sessions.
    public double? CrashFreeSessionsPct { get; set; }

    public List<CountByKey> EventsByLevel { get; set; } = [];
    public List<CountByKey> EventsByRelease { get; set; } = [];
    public List<DailyCount> EventsPerDay { get; set; } = [];
}

public class CountByKey
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DailyCount
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}
