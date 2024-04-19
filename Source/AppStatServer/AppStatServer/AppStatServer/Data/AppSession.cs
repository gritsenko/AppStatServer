namespace AppStatServer.Data;

public class AppSession
{
    public string Id { get; set; }
    public DateTime Started { get; set; }
    public DateTime Timestamp { get; set; }
    public int Duration { get; set; }
    public int Errors { get; set; }
    public string Release { get; set; }
    public string Environment { get; set; }
    public string Os { get; set; }
}