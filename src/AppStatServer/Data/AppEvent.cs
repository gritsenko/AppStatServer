namespace AppStatServer.Data;

public class AppEvent
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }

    public string? EventEntry { get; set; }
    public string? StackTrace { get; set; }
    public string Release { get; set; } = string.Empty;

    public bool IsCrash { get; set; }
    public bool IsError { get; set; }

    // An Application Not Responding report. Kept apart from a plain crash because it is a
    // different failure (the app was alive but wedged) and is triaged from a thread dump
    // rather than a stack.
    public bool IsAnr { get; set; }

    public string Message { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string? SpanId { get; set; }
    public string? TraceId { get; set; }
    public string? Os { get; set; }
    public string? DeviceModel { get; set; }
    public string? Arch { get; set; }
    public string UserId { get; set; } = string.Empty;
}
