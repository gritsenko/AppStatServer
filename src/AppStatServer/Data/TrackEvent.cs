namespace AppStatServer.Data;

// A custom product-analytics event (AppCenter trackEvent-style): a name plus a bag of
// typed properties. Unlike AppEvent (crash/error shaped) this carries no stack trace —
// it exists for funnels/segmentation, and shares UserId/SessionId/Release/Os with
// AppEvent and AppSession so events can be joined to sessions and crashes.
public class TrackEvent
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }

    public string Name { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public string? Os { get; set; }

    // Values are scalar CLR primitives (string / long / double / bool). Kept as object so
    // numeric props can be summed/averaged (price) and booleans filtered downstream.
    public Dictionary<string, object> Properties { get; set; } = new();
}
