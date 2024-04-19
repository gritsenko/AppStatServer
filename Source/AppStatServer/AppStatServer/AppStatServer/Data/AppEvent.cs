using AppStatServer.Sentry;

namespace AppStatServer.Data;

public class AppEvent
{
    public string Id { get; set; }
    public DateTime Timestamp { get; set; }

    public string? EventEntry { get; set; }
    public string Release { get; set; }

    public bool IsCrash { get; set; }
    public bool IsError { get; set; }

    public string Message { get; set; }
    public string SessionId { get; set; }

    public string Level { get; set; }

    public string? SpanId { get; set; }
    public string? TraceId { get; set; }
    public string? Os { get; set; }
    public string UserId { get; set; }
    public string? StackTrace { get; set; }
}