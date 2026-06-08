namespace AppStatServerLite.Sentry;

public class Attrs
{
    public string release { get; set; }
    public string environment { get; set; }
}

public class SessionEntry
{
    public string sid { get; set; }
    public string did { get; set; }
    public bool init { get; set; }
    public DateTime started { get; set; }
    public DateTime timestamp { get; set; }
    public int seq { get; set; }
    public int duration { get; set; }
    public int errors { get; set; }
    public Attrs attrs { get; set; }
}
