namespace AppStatServer.Data;

// AppCenter-style usage analytics over a rolling window of days.
public class AnalyticsData
{
    public int Days { get; set; }

    public int Mau { get; set; }            // distinct active users in the whole window
    public int Wau { get; set; }            // distinct active users in the last 7 days
    public int Dau { get; set; }            // distinct active users on the most recent day
    public int NewUsers { get; set; }       // users whose first-ever event falls in the window

    public int TotalSessions { get; set; }
    public double AvgSessionSeconds { get; set; }
    public double SessionsPerUser { get; set; }

    public List<DayPoint> UsersPerDay { get; set; } = [];       // active + new, per day
    public List<DailyCount> SessionsPerDay { get; set; } = [];
    public List<CountByKey> DurationBuckets { get; set; } = []; // session length histogram
    public List<CountByKey> VersionDistribution { get; set; } = []; // active users per release
    public List<CountByKey> OsDistribution { get; set; } = [];   // active users per OS
    public List<CountByKey> DeviceDistribution { get; set; } = []; // active users per device model
}

// Distinct values available for the version / OS filters on the diagnostics pages.
public class Facets
{
    public List<string> Releases { get; set; } = [];
    public List<string> Oses { get; set; } = [];
}

public class DayPoint
{
    public string Date { get; set; } = string.Empty;
    public int Active { get; set; }
    public int NewUsers { get; set; }
}

// A collapsed group of similar events (AppCenter-style event/crash grouping).
public class EventGroup
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public bool IsCrash { get; set; }

    public int Count { get; set; }          // total occurrences
    public int Users { get; set; }          // distinct affected users
    public string? Release { get; set; }    // release of the latest occurrence

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public AppEvent Sample { get; set; } = new(); // latest occurrence (carries the stack trace)
}

// Product-analytics report over the custom track-events collection, for a rolling window.
public class EventReport
{
    public int Days { get; set; }
    public int TotalEvents { get; set; }
    public int DistinctNames { get; set; }   // number of distinct event names
    public int Users { get; set; }           // distinct users across all events in the window
    public List<DailyCount> EventsPerDay { get; set; } = [];
    public List<EventStat> Events { get; set; } = []; // one row per event name, most frequent first

    // Distinct values available for the platform / version filters, taken over the whole
    // window (before the current release/os filter) so the dropdowns stay stable.
    public List<string> Releases { get; set; } = [];
    public List<string> Oses { get; set; } = [];
}

// One event name rolled up: how often, by how many users, and its property value distribution.
public class EventStat
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Users { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public List<PropertyBreakdown> Properties { get; set; } = [];
}

// Value distribution for a single property key of an event (e.g. productId -> {pro: 12, lite: 3}).
public class PropertyBreakdown
{
    public string Key { get; set; } = string.Empty;
    public List<CountByKey> Values { get; set; } = [];
}
