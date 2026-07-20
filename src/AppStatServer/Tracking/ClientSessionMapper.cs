using System.Globalization;
using AppStatServer.Data;

namespace AppStatServer.Tracking;

// Turns a parsed "@session" TrackEvent (active-time ping) into a ClientSession. The identity fields
// (Id/UserId/Release/Os) come from the batch envelope already applied to the TrackEvent; the
// counters and start time come from the event properties. Returns null if the ping lacks the
// counters (nothing useful to record).
public static class ClientSessionMapper
{
    // Reserved-event name prefix: infrastructure events the client sends over /api/track that must
    // never be listed among product events. "@session" is the only one today.
    public const char ReservedPrefix = '@';
    public const string SessionEventName = "@session";

    public static bool IsReserved(string? name) => !string.IsNullOrEmpty(name) && name[0] == ReservedPrefix;

    public static ClientSession? FromTrackEvent(TrackEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionId))
            return null;

        // Counters are sent as JSON numbers → parsed to long by TrackEventParser.
        if (!TryGetLong(e.Properties, "activeSeconds", out var active) ||
            !TryGetLong(e.Properties, "wallSeconds", out var wall))
            return null;

        // startedUtc is sent as an ISO-8601 string; normalise to local time to match the rest of the
        // analytics (LiteDB stores local). Fall back to the event timestamp if absent/unparseable.
        var started = TryGetUtc(e.Properties, "startedUtc", out var utc) ? utc.ToLocalTime() : e.Timestamp;

        return new ClientSession
        {
            Id = e.SessionId,
            UserId = e.UserId,
            Release = e.Release,
            Os = e.Os,
            Platform = e.Properties.TryGetValue("platform", out var p) ? p as string : null,
            Started = started,
            LastSeen = e.Timestamp,
            ActiveSeconds = Math.Max(0, active),
            WallSeconds = Math.Max(0, wall),
        };
    }

    private static bool TryGetLong(IReadOnlyDictionary<string, object> props, string key, out long value)
    {
        value = 0;
        if (!props.TryGetValue(key, out var raw))
            return false;
        switch (raw)
        {
            case long l: value = l; return true;
            case int i: value = i; return true;
            case double d: value = (long)d; return true;
            default: return false;
        }
    }

    private static bool TryGetUtc(IReadOnlyDictionary<string, object> props, string key, out DateTime value)
    {
        value = default;
        if (props.TryGetValue(key, out var raw) && raw is string s &&
            DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
