namespace AppStatServerLite.Sentry;

public class Sdk
{
    public string name { get; set; }
    public string version { get; set; }
}
public class Trace
{
    public string trace_id { get; set; }
    public string public_key { get; set; }
    public string release { get; set; }
    public string environment { get; set; }
}

public class SdkEntry
{
    public Sdk sdk { get; set; }
    public string event_id { get; set; }
    public Trace trace { get; set; }
    public DateTime sent_at { get; set; }
}