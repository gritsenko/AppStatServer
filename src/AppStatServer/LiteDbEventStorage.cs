using System.Collections.Immutable;
using System.Globalization;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer;

public class LiteDbEventStorage : IEventStorage, IDisposable
{
    private readonly LiteDatabase _db;

    // The on-disk path of the database, kept so the Maintenance page can report its size and
    // the drive it lives on. Null for the in-memory (test) database, which has no file.
    private readonly string? _dbFilePath;

    // Friendly names for the collections shown on the Maintenance page's storage breakdown.
    private static readonly Dictionary<string, string> CollectionLabels = new()
    {
        ["events"] = "Logs & crashes",
        ["sessions"] = "Sessions",
        ["trackevents"] = "Custom events",
        ["resolutions"] = "Resolutions",
    };

    public LiteDbEventStorage(string? dbFileName = "AppStat.db")
        : this(new LiteDatabase(dbFileName ?? "AppStat.db"), dbFileName ?? "AppStat.db")
    {
    }

    // Allows injecting an in-memory LiteDatabase (e.g. new LiteDatabase(new MemoryStream())) for tests.
    public LiteDbEventStorage(LiteDatabase db)
        : this(db, null)
    {
    }

    private LiteDbEventStorage(LiteDatabase db, string? dbFilePath)
    {
        _db = db;
        _dbFilePath = dbFilePath;
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

    public Task<EventReport> GetEventReportAsync(int days, string? release = null, string? os = null)
    {
        var col = _db.GetCollection<TrackEvent>("trackevents");

        // Same local-time window handling as GetAnalyticsAsync.
        var today = DateTime.Now.Date;
        var start = today.AddDays(-(days - 1));
        var dayList = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();

        var windowed = col.Find(e => e.Timestamp >= start).ToList();

        // Filter facets are computed over the whole window (before release/os narrowing),
        // so selecting one value doesn't collapse the other dropdown's options. The raw OS /
        // user-agent strings are collapsed into platform buckets (Windows, Android, Web, …)
        // so the dropdown lists a handful of platforms instead of every distinct OS build.
        var releases = windowed.Where(e => !string.IsNullOrEmpty(e.Release))
            .Select(e => e.Release).Distinct().OrderBy(x => x).ToList();
        var oses = windowed
            .GroupBy(e => Platform.Categorize(e.Os))
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        var all = windowed;
        if (!string.IsNullOrEmpty(release))
            all = all.Where(e => e.Release == release).ToList();
        // The `os` filter now carries a platform bucket rather than a raw OS string.
        if (!string.IsNullOrEmpty(os))
            all = all.Where(e => Platform.Categorize(e.Os) == os).ToList();

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

        // Tag each event with its platform once, then roll up: which platforms appear (most
        // events first, to order the stack) and each day's per-platform event counts.
        var tagged = all.Select(e => (e.Timestamp.Date, Platform: Platform.Categorize(e.Os))).ToList();
        var platforms = tagged
            .GroupBy(t => t.Platform)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();
        var platformsPerDay = dayList.Select(d => new PlatformDay
        {
            Date = d.ToString("yyyy-MM-dd"),
            Counts = tagged.Where(t => t.Date == d)
                .GroupBy(t => t.Platform)
                .ToDictionary(g => g.Key, g => g.Count()),
        }).ToList();

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
            Platforms = platforms,
            PlatformsPerDay = platformsPerDay,
            Releases = releases,
            Oses = oses,
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
        var trackEvents = _db.GetCollection<TrackEvent>("trackevents");

        // Pull events once and compute the breakdowns in memory — fine for the
        // proof-of-concept data volumes this server is expected to hold.
        var all = events.FindAll().ToList();
        var track = trackEvents.FindAll().ToList();

        // Same local-time anchoring as GetAnalyticsAsync (LiteDB returns local DateTimes).
        var today = DateTime.Now.Date;
        var last7 = today.AddDays(-6);
        var prev7 = today.AddDays(-13);
        var recentSessions = sessions.Find(s => s.Started >= last7).ToList();

        var stats = new DashboardStats
        {
            TotalEvents = all.Count,
            Errors = all.Count(e => e.IsError),
            Crashes = all.Count(e => e.IsCrash),
            TotalSessions = sessions.Count(),
            CustomEvents = track.Count,
            EventsToday = all.Count(e => e.Timestamp >= today),
            ErrorsLast7Days = all.Count(e => e.IsError && e.Timestamp >= last7),
            ErrorsPrev7Days = all.Count(e => e.IsError && e.Timestamp >= prev7 && e.Timestamp < last7),
            CrashesLast7Days = all.Count(e => e.IsCrash && e.Timestamp >= last7),
            CrashesPrev7Days = all.Count(e => e.IsCrash && e.Timestamp >= prev7 && e.Timestamp < last7),
            SessionsLast7Days = recentSessions.Count,
            CrashFreeSessionsPct = recentSessions.Count > 0
                ? 100.0 * recentSessions.Count(s => s.Errors == 0) / recentSessions.Count
                : null,
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
            // Take the last 14 days that carry any activity — Sentry events OR custom
            // track events — and report both counts per day so the chart can stack them.
            EventsPerDay = all.Select(e => e.Timestamp.Date)
                .Concat(track.Select(t => t.Timestamp.Date))
                .Distinct()
                .OrderBy(d => d)
                .TakeLast(14)
                .Select(d => new DailyEventCount
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Events = all.Count(e => e.Timestamp.Date == d),
                    Custom = track.Count(t => t.Timestamp.Date == d),
                })
                .ToList(),
        };

        return Task.FromResult(stats);
    }

    // A minimal per-user activity signal, projected from either an AppEvent (Sentry) or a
    // TrackEvent (custom product event), so usage analytics reflect real engagement rather
    // than crashes/errors alone. (Device model is intentionally omitted — track events carry none.)
    private readonly record struct UserActivity(string UserId, DateTime Timestamp, string Release, string? Os);

    public Task<AnalyticsData> GetAnalyticsAsync(int days)
    {
        var events = _db.GetCollection<AppEvent>("events");
        var sessions = _db.GetCollection<AppSession>("sessions");
        var trackEvents = _db.GetCollection<TrackEvent>("trackevents");

        // LiteDB returns DateTimes as local time, so anchor the window on local "now".
        var today = DateTime.Now.Date;
        var start = today.AddDays(-(days - 1));
        var dayList = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();

        // Any user-attributed signal counts as activity — Sentry events AND custom product
        // events — so a user who never crashes still registers as active.
        var appEvents = events.FindAll().ToList();
        var activity = appEvents
            .Select(e => new UserActivity(e.UserId, e.Timestamp, e.Release, e.Os))
            .Concat(trackEvents.FindAll().Select(t => new UserActivity(t.UserId, t.Timestamp, t.Release, t.Os)))
            .Where(a => !string.IsNullOrEmpty(a.UserId))
            .ToList();

        // First-ever activity per user (across all of history) tells us who is "new" in the window.
        var firstSeen = activity
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Timestamp));

        var winActivity = activity.Where(a => a.Timestamp >= start).ToList();
        var winSessions = sessions.Find(s => s.Started >= start).ToList();

        var activeByDay = winActivity
            .GroupBy(a => a.Timestamp.Date)
            .ToDictionary(g => g.Key, g => g.Select(a => a.UserId).Distinct().Count());
        var newByDay = firstSeen
            .Where(kv => kv.Value >= start)
            .GroupBy(kv => kv.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var week = today.AddDays(-6);

        // Device model is Sentry-only, so its distribution is computed from AppEvents in-window.
        var winDeviceEvents = appEvents.Where(e => !string.IsNullOrEmpty(e.UserId) && e.Timestamp >= start).ToList();

        var data = new AnalyticsData
        {
            Days = days,
            Mau = winActivity.Select(a => a.UserId).Distinct().Count(),
            Wau = winActivity.Where(a => a.Timestamp >= week).Select(a => a.UserId).Distinct().Count(),
            Dau = winActivity.Where(a => a.Timestamp >= today).Select(a => a.UserId).Distinct().Count(),
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
            VersionDistribution = winActivity
                .Where(a => !string.IsNullOrEmpty(a.Release))
                .GroupBy(a => a.Release)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Select(a => a.UserId).Distinct().Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
            OsDistribution = winActivity
                .Where(a => !string.IsNullOrEmpty(a.Os))
                .GroupBy(a => a.Os!)
                .Select(g => new CountByKey { Key = g.Key, Count = g.Select(a => a.UserId).Distinct().Count() })
                .OrderByDescending(c => c.Count)
                .Take(10)
                .ToList(),
            DeviceDistribution = winDeviceEvents
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

    public Task<DiagnosticsReport> GetDiagnosticsAsync(int days, string? release = null)
    {
        var col = _db.GetCollection<AppEvent>("events");

        // Same local-time window handling as GetAnalyticsAsync.
        var today = DateTime.Now.Date;
        var start = today.AddDays(-(days - 1));
        var dayList = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();

        var all = col.Find(e => e.Timestamp >= start).ToList();
        if (!string.IsNullOrEmpty(release))
            all = all.Where(e => e.Release == release).ToList();

        // Crashes = unhandled (a thread crashed). Errors = handled exceptions the app
        // reported without terminating. The two are disjoint, matching AppCenter.
        var crashes = all.Where(e => e.IsCrash).ToList();
        var errors = all.Where(e => e.IsError && !e.IsCrash).ToList();

        var resolutions = _db.GetCollection<Resolution>("resolutions").FindAll()
            .GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ResolvedAt));

        var groups = BuildDiagnosticGroups(crashes, "crash", resolutions)
            .Concat(BuildDiagnosticGroups(errors, "error", resolutions))
            .OrderByDescending(g => g.LastSeen)
            .Take(300)
            .ToList();

        var report = new DiagnosticsReport
        {
            Days = days,
            Release = release,
            TotalCrashes = crashes.Count,
            TotalErrors = errors.Count,
            AffectedUsers = crashes.Concat(errors)
                .Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count(),
            OpenGroups = groups.Count(g => !g.Resolved),
            CrashesPerDay = dayList.Select(d => new DailyCount
            {
                Date = d.ToString("yyyy-MM-dd"),
                Count = crashes.Count(e => e.Timestamp.Date == d),
            }).ToList(),
            ErrorsPerDay = dayList.Select(d => new DailyCount
            {
                Date = d.ToString("yyyy-MM-dd"),
                Count = errors.Count(e => e.Timestamp.Date == d),
            }).ToList(),
            Groups = groups,
        };

        return Task.FromResult(report);
    }

    // Collapse events into per-message signatures, tagging each with its resolution state.
    private static List<DiagnosticGroup> BuildDiagnosticGroups(
        List<AppEvent> events, string kind, IReadOnlyDictionary<string, DateTime> resolutions)
    {
        return events
            .GroupBy(e => kind + "|" + e.Message)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Timestamp).First();
                latest.EventEntry = null; // trim the bulky raw payload from the sample

                var lastSeen = g.Max(e => e.Timestamp);
                var resolvedAt = resolutions.TryGetValue(g.Key, out var at) ? at : (DateTime?)null;

                return new DiagnosticGroup
                {
                    Key = g.Key,
                    Kind = kind,
                    Title = latest.Message,
                    Level = latest.Level,
                    Count = g.Count(),
                    Users = g.Select(e => e.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count(),
                    Release = latest.Release,
                    FirstSeen = g.Min(e => e.Timestamp),
                    LastSeen = lastSeen,
                    // Resolved only while nothing newer has come in — a recurrence reopens it.
                    Resolved = resolvedAt.HasValue && lastSeen <= resolvedAt.Value,
                    ResolvedAt = resolvedAt,
                    Sample = latest,
                };
            })
            .ToList();
    }

    public Task<bool> SetResolutionAsync(string key, bool resolved)
    {
        var col = _db.GetCollection<Resolution>("resolutions");
        col.EnsureIndex(r => r.Key);
        col.DeleteMany(r => r.Key == key);
        if (resolved)
            col.Insert(new Resolution { Key = key, ResolvedAt = DateTime.Now });
        return Task.FromResult(resolved);
    }

    public Task<StorageInfo> GetStorageInfoAsync()
    {
        var info = new StorageInfo();

        if (!string.IsNullOrEmpty(_dbFilePath))
        {
            var full = Path.GetFullPath(_dbFilePath);
            info.DatabasePath = full;
            if (File.Exists(full))
                info.DatabaseFileBytes = new FileInfo(full).Length;
        }

        // Walk every collection actually present in the file (empty on a fresh DB) and sum the
        // serialized size of its documents. GetBytesCount is the logical BSON size, so the
        // per-collection totals come to less than the file — the difference is index and page
        // overhead, which shows up as the gap between DataBytes and DatabaseFileBytes.
        foreach (var name in _db.GetCollectionNames())
        {
            var col = _db.GetCollection(name);
            long docs = 0, bytes = 0;
            foreach (var doc in col.FindAll())
            {
                docs++;
                bytes += BsonSerializer.Serialize(doc).Length;
            }

            info.Collections.Add(new CollectionInfo
            {
                Name = name,
                Label = CollectionLabels.TryGetValue(name, out var label) ? label : name,
                Documents = docs,
                Bytes = bytes,
            });
            info.DataBytes += bytes;
        }

        info.Collections = info.Collections.OrderByDescending(c => c.Bytes).ToList();

        return Task.FromResult(info);
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
