namespace AppStatServer.Data;

// Wire shape for the Maintenance page's "delete old data" action.
public record PurgeRequest(int OlderThanDays);

// What a purge removed (or, for a preview, would remove) per collection. Bytes is the
// logical BSON size of the matched documents — the file itself shrinks only after a compact.
public class PurgeResult
{
    public int OlderThanDays { get; set; }
    public int Events { get; set; }
    public int Sessions { get; set; }
    public int TrackEvents { get; set; }
    public long Bytes { get; set; }
    public int Total => Events + Sessions + TrackEvents;
}

// Database file size around a compaction; both zero for the in-memory (test) database.
public class CompactResult
{
    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }
}
