using System.Collections.Immutable;
using System.Globalization;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer;

public class LiteDbEventStorage : IEventStorage, IDisposable
{
    private readonly LiteDatabase _db;

    public LiteDbEventStorage(string? dbFileName = "AppStat.db")
        : this(new LiteDatabase(dbFileName ?? "AppStat.db"))
    {
    }

    // Allows injecting an in-memory LiteDatabase (e.g. new LiteDatabase(new MemoryStream())) for tests.
    public LiteDbEventStorage(LiteDatabase db)
    {
        _db = db;
    }

    public Task SaveEventsAsync(IEnumerable<AppEvent> appEvents)
    {
        var col = _db.GetCollection<AppEvent>("events");
        col.Insert(appEvents);
        return Task.CompletedTask;
    }

    public Task<ImmutableList<AppEvent>> GetRecentEventsAsync()
    {
        var col = _db.GetCollection<AppEvent>("events");
        var result = col.Find(x => true, 0, 100);
        return Task.FromResult(result.ToImmutableList());
    }

    public Task SaveSessionsAsync(IEnumerable<AppSession> sessions)
    {
        var col = _db.GetCollection<AppSession>("sessions");
        // Sessions are sent multiple times per session id (init -> update -> end),
        // so upsert to keep the latest state instead of throwing on duplicate ids.
        col.Upsert(sessions);
        return Task.CompletedTask;
    }

    public Task<ImmutableList<AppSession>> GetRecentSessionsAsync()
    {
        var col = _db.GetCollection<AppSession>("sessions");
        var result = col.Find(x => true, 0, 100);
        return Task.FromResult(result.ToImmutableList());
    }

    public Task SaveTrackEventsAsync(IEnumerable<TrackEvent> trackEvents)
    {
        var col = _db.GetCollection<TrackEvent>("trackevents");
        col.Insert(trackEvents);
        return Task.CompletedTask;
    }

    public Task<ImmutableList<TrackEvent>> GetRecentTrackEventsAsync()
    {
        var col = _db.GetCollection<TrackEvent>("trackevents");
        var result = col.Find(x => true, 0, 100);
        return Task.FromResult(result.ToImmutableList());
    }

    public Task<EventReport> GetEventReportAsync(int days)
    {
        var col = _db.GetCollection<TrackEvent>("trackevents");

        // Same local-time window handling as GetAnalyticsAsync.
        var today = DateTime.Now.Date;
        var start = today.AddDays(-(days - 1));
        var dayList = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();

        var all = col.Find(e => e.Timestamp >= start).ToList();

        var perName = all
            .GroupBy(e => e.Name)
            .Select(g => new EventStat
            {
                Name = g.Key,
                Count = g.Count(),
                Users = g.Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count(),
                FirstSeen = g.Min(e => e.Timestamp),
                LastSeen = g.Max(e => e.Timestamp),
                Properties = BuildPropertyBreakdowns(g),
            })
            .OrderByDescending(s => s.Count)
            .ToList();

        var report = new EventReport
        {
            Days = days,
            TotalEvents = all.Count,
            DistinctNames = perName.Count,
            Users = all.Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count(),
            EventsPerDay = dayList.Select(d => new DailyCount
            {
                Date = d.ToString("yyyy-MM-dd"),
                Count = all.Count(e => e.Timestamp.Date == d),
            }).ToList(),
            Events = perName,
        };

        return Task.FromResult(report);
    }

    // Per-property value distribution for one event name: top values by frequency, capped
    // so a high-cardinality property (e.g. a raw price) can't blow up the response payload.
    private static List<PropertyBreakdown> BuildPropertyBreakdowns(IEnumerable<TrackEvent> events)
    {
        const int maxKeys = 12;
        const int maxValues = 8;

        var byKey = new Dictionary<string, Dictionary<string, int>>();
        foreach (var e in events)
            foreach (var (key, value) in e.Properties)
            {
                if (!byKey.TryGetValue(key, out var counts))
                    byKey[key] = counts = new Dictionary<string, int>();
                var label = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                counts[label] = counts.GetValueOrDefault(label) + 1;
            }

        return byKey
            .OrderByDescending(kv => kv.Value.Values.Sum())
            .Take(maxKeys)
            .Select(kv => new PropertyBreakdown
            {
                Key = kv.Key,
                Values = kv.Value
                    .OrderByDescending(v => v.Value)
                    .Take(maxValues)
                    .Select(v => new CountByKey { Key = v.Key, Count = v.Value })
                    .ToList(),
            })
            .ToList();
    }

    public Task<DashboardStats> GetStatsAsync()
    {
        var events = _db.GetCollection<AppEvent>("events");
        var sessions = _db.GetCollection<AppSession>("sessions");

        // Pull events once and compute the breakdowns in memory — fine for the
        // proof-of-concept data volumes this server is expected to hold.
        var all = events.FindAll().ToList();

        var stats = new DashboardStats
        {
            TotalEvents = all.Count,
            Errors = all.Count(e => e.IsError),
            Crashes = all.Count(e => e.IsCrash),
            TotalSessions = sessions.Count(),
            EventsByLevel = all
                .GroupBy(e => string.IsNullOrEmpty(e.Level) ? "-" : e.Level)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .ToList(),
            EventsByRelease = all
                .Where(e => !string.IsNullOrEmpty(e.Release))
                .GroupBy(e => e.Release)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
            EventsPerDay = all
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key)
                .TakeLast(14)
                .Select(g => new DailyCount { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
                .ToList(),
        };

        return Task.FromResult(stats);
    }

    public Task<AnalyticsData> GetAnalyticsAsync(int days)
    {
        var events = _db.GetCollection<AppEvent>("events");
        var sessions = _db.GetCollection<AppSession>("sessions");

        // LiteDB returns DateTimes as local time, so anchor the window on local "now".
        var today = DateTime.Now.Date;
        var start = today.AddDays(-(days - 1));
        var dayList = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();

        var allEvents = events.FindAll().Where(e => !string.IsNullOrEmpty(e.UserId)).ToList();

        // First-ever event per user (across all of history) tells us who is "new" in the window.
        var firstSeen = allEvents
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Min(e => e.Timestamp));

        var winEvents = allEvents.Where(e => e.Timestamp >= start).ToList();
        var winSessions = sessions.Find(s => s.Started >= start).ToList();

        var activeByDay = winEvents
            .GroupBy(e => e.Timestamp.Date)
            .ToDictionary(g => g.Key, g => g.Select(e => e.UserId).Distinct().Count());
        var newByDay = firstSeen
            .Where(kv => kv.Value >= start)
            .GroupBy(kv => kv.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var week = today.AddDays(-6);

        var data = new AnalyticsData
        {
            Days = days,
            Mau = winEvents.Select(e => e.UserId).Distinct().Count(),
            Wau = winEvents.Where(e => e.Timestamp >= week).Select(e => e.UserId).Distinct().Count(),
            Dau = winEvents.Where(e => e.Timestamp >= today).Select(e => e.UserId).Distinct().Count(),
            NewUsers = firstSeen.Count(kv => kv.Value >= start),
            TotalSessions = winSessions.Count,
            AvgSessionSeconds = winSessions.Count > 0 ? winSessions.Average(s => s.Duration) : 0,
            UsersPerDay = dayList.Select(d => new DayPoint
            {
                Date = d.ToString("yyyy-MM-dd"),
                Active = activeByDay.GetValueOrDefault(d, 0),
                NewUsers = newByDay.GetValueOrDefault(d, 0),
            }).ToList(),
            SessionsPerDay = dayList.Select(d => new DailyCount
            {
                Date = d.ToString("yyyy-MM-dd"),
                Count = winSessions.Count(s => s.Started.Date == d),
            }).ToList(),
            DurationBuckets = BuildDurationBuckets(winSessions),
            VersionDistribution = winEvents
                .Where(e => !string.IsNullOrEmpty(e.Release))
                .GroupBy(e => e.Release)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Select(e => e.UserId).Distinct().Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
            OsDistribution = winEvents
                .Where(e => !string.IsNullOrEmpty(e.Os))
                .GroupBy(e => e.Os!)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Select(e => e.UserId).Distinct().Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
            DeviceDistribution = winEvents
                .Where(e => !string.IsNullOrEmpty(e.DeviceModel))
                .GroupBy(e => e.DeviceModel!)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Select(e => e.UserId).Distinct().Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
        };

        data.SessionsPerUser = data.Mau > 0 ? (double)data.TotalSessions / data.Mau : 0;

        return Task.FromResult(data);
    }

    public Task<ImmutableList<EventGroup>> GetEventGroupsAsync(bool crashesOnly, string? release = null, string? os = null)
    {
        var col = _db.GetCollection<AppEvent>("events");
        var items = (crashesOnly ? col.Find(e => e.IsCrash) : col.Find(e => !e.IsCrash)).ToList();

        if (!string.IsNullOrEmpty(release))
            items = items.Where(e => e.Release == release).ToList();
        if (!string.IsNullOrEmpty(os))
            items = items.Where(e => e.Os == os).ToList();

        var groups = items
            .GroupBy(e => crashesOnly ? "crash|" + e.Message : e.Level + "|" + e.Message)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Timestamp).First();
                latest.EventEntry = null; // trim the bulky raw payload from the sample

                return new EventGroup
                {
                    Key = g.Key,
                    Title = latest.Message,
                    Level = latest.Level,
                    IsCrash = crashesOnly,
                    Count = g.Count(),
                    Users = g.Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count(),
                    Release = latest.Release,
                    FirstSeen = g.Min(e => e.Timestamp),
                    LastSeen = g.Max(e => e.Timestamp),
                    Sample = latest,
                };
            })
            .OrderByDescending(x => x.LastSeen)
            .Take(200)
            .ToImmutableList();

        return Task.FromResult(groups);
    }

    public Task<Facets> GetFacetsAsync()
    {
        var all = _db.GetCollection<AppEvent>("events").FindAll().ToList();
        var facets = new Facets
        {
            Releases = all.Where(e => !string.IsNullOrEmpty(e.Release))
                .Select(e => e.Release).Distinct().OrderBy(x => x).ToList(),
            Oses = all.Where(e => !string.IsNullOrEmpty(e.Os))
                .Select(e => e.Os!).Distinct().OrderBy(x => x).ToList(),
        };
        return Task.FromResult(facets);
    }

    private static List<CountByKey> BuildDurationBuckets(List<AppSession> sessions)
    {
        (string Label, Func<int, bool> Match)[] buckets =
        [
            ("0-10s", d => d < 10),
            ("10-30s", d => d is >= 10 and < 30),
            ("30s-1m", d => d is >= 30 and < 60),
            ("1-30m", d => d is >= 60 and < 1800),
            ("30m-1h", d => d is >= 1800 and < 3600),
            (">1h", d => d >= 3600),
        ];

        return buckets
            .Select(b => new CountByKey { Key = b.Label, Count = sessions.Count(s => b.Match(s.Duration)) })
            .ToList();
    }

    public void Dispose() => _db.Dispose();
}
