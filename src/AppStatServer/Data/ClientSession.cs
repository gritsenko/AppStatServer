namespace AppStatServer.Data;

// One app-run session as reported by the client's active-time pings ("@session" events on
// /api/track). Unlike AppSession (Sentry release-health, wall-clock only), this carries the app's
// real ACTIVE usage time — foreground and not idle — so the dashboard can show engagement instead
// of "how long the process was alive".
//
// Counters are cumulative-since-process-start and pings arrive repeatedly, so records are upserted
// by Id with a max-merge (see LiteDbEventStorage.SaveClientSessionsAsync) — duplicate / out-of-order
// delivery is harmless. Id == the AppStat envelope sessionId, so a session joins its TrackEvents
// exactly on SessionId. (This is a DIFFERENT id space from Sentry's AppSession.Id — the two session
// datasets are not joined per-session.)
public class ClientSession
{
    public string Id { get; set; } = string.Empty;      // envelope sessionId (GUID "N")
    public string UserId { get; set; } = string.Empty;  // stable anonymous install id
    public string Release { get; set; } = string.Empty;
    public string? Os { get; set; }
    public string? Platform { get; set; }               // head platform (Desktop / Android / WASM)

    // Stored in local time to match the rest of the analytics (LiteDB persists local, and
    // GetAnalyticsAsync windows on local "now"), despite the wire property being "startedUtc".
    public DateTime Started { get; set; }
    public DateTime LastSeen { get; set; }               // timestamp of the latest ping

    public long ActiveSeconds { get; set; }              // foreground & not-idle time (max over pings)
    public long WallSeconds { get; set; }                // process wall-clock (max over pings)
}

// A ClientSession enriched with how many product events fired during it (joined by SessionId).
// Feeds the dashboard's recent-sessions table.
public class ClientSessionRow
{
    public ClientSession Session { get; set; } = new();
    public int EventCount { get; set; }
}

// A ClientSession plus its product-event timeline — the per-session drill-down.
public class ClientSessionDetail
{
    public ClientSession Session { get; set; } = new();
    public List<SessionEvent> Events { get; set; } = [];
}

// One product event within a session, with its offset from the session start for the timeline.
public class SessionEvent
{
    public string Name { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int OffsetSeconds { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}
