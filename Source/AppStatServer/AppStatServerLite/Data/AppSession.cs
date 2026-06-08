namespace AppStatServerLite.Data;

public class AppSession
{
    public string Id { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public DateTime Started { get; set; }
    public DateTime Timestamp { get; set; }
    public int Seq { get; set; }
    public int Duration { get; set; }
    public int Errors { get; set; }
    public bool Init { get; set; }
    public string Release { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
