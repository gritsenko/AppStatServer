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
