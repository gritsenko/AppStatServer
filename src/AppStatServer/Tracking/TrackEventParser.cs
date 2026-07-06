using System.Text.Json;
using AppStatServer.Data;

namespace AppStatServer.Tracking;

/// <summary>
/// Pure (no I/O) parser for a decompressed /api/track batch body.
/// The wire shape hoists the identity fields shared by every event in the batch
/// (userId/sessionId/release/os) to the top level and lists the events under "events":
/// <code>
/// { "userId": "...", "sessionId": "...", "release": "1.2.3", "os": "Windows 11",
///   "events": [ { "name": "buy_clicked", "timestamp": "...", "properties": { "productId": "pro", "price": 4.99 } } ] }
/// </code>
/// Kept free of HTTP/storage concerns so it can be unit tested directly.
/// </summary>
public static class TrackEventParser
{
    // AppCenter Analytics limits, enforced server-side as defense in depth against a buggy
    // client flooding storage: events are truncated/trimmed rather than rejected.
    public const int MaxNameLength = 256;
    public const int MaxProperties = 20;
    public const int MaxKeyLength = 125;
    public const int MaxValueLength = 125;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static List<TrackEvent> Parse(string body)
    {
        var result = new List<TrackEvent>();

        if (string.IsNullOrWhiteSpace(body))
            return result;

        TrackBatch? batch;
        try
        {
            batch = JsonSerializer.Deserialize<TrackBatch>(body, Options);
        }
        catch (JsonException)
        {
            return result; // malformed body — drop the whole batch rather than 500
        }

        if (batch?.Events == null)
            return result;

        foreach (var entry in batch.Events)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            result.Add(new TrackEvent
            {
                Id = Guid.NewGuid().ToString(),
                Name = Truncate(entry.Name!, MaxNameLength),
                // Client sends UTC; the rest of the analytics anchors on local time (LiteDB
                // stores local), so normalise here. Fall back to receive-time if absent.
                Timestamp = entry.Timestamp?.ToLocalTime() ?? DateTime.Now,
                UserId = batch.UserId ?? string.Empty,
                SessionId = batch.SessionId ?? string.Empty,
                Release = batch.Release ?? string.Empty,
                Os = batch.Os,
                Properties = NormalizeProperties(entry.Properties),
            });
        }

        return result;
    }

    private static Dictionary<string, object> NormalizeProperties(Dictionary<string, JsonElement>? props)
    {
        var result = new Dictionary<string, object>();
        if (props == null)
            return result;

        foreach (var (key, element) in props)
        {
            if (result.Count >= MaxProperties)
                break;
            if (string.IsNullOrEmpty(key))
                continue;

            var value = ToClrValue(element);
            if (value == null) // drop null/object/array — analytics properties are scalar
                continue;

            result[Truncate(key, MaxKeyLength)] = value;
        }

        return result;
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => Truncate(element.GetString() ?? string.Empty, MaxValueLength),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    // Wire DTOs (deserialization only): any field may be absent, so all are nullable.
    private sealed class TrackBatch
    {
        public string? UserId { get; set; }
        public string? SessionId { get; set; }
        public string? Release { get; set; }
        public string? Os { get; set; }
        public List<TrackEntry>? Events { get; set; }
    }

    private sealed class TrackEntry
    {
        public string? Name { get; set; }
        public DateTime? Timestamp { get; set; }
        public Dictionary<string, JsonElement>? Properties { get; set; }
    }
}
