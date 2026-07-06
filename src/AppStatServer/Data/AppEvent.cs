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

    public string Message { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string? SpanId { get; set; }
    public string? TraceId { get; set; }
    public string? Os { get; set; }
    public string? DeviceModel { get; set; }
    public string UserId { get; set; } = string.Empty;
}
