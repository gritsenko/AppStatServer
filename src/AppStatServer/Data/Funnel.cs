namespace AppStatServer.Data;

// A saved conversion funnel: an ordered list of custom-event names. A user "converts"
// through step k when they have a step-k event at or after their step-(k-1) event, so the
// report counts ordered sequences, not mere co-occurrence.
public class Funnel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

// Wire shape for creating a funnel from the dashboard.
public record FunnelCreateRequest(string? Name, List<string>? Steps);

// The computed conversion report for one funnel over a rolling window.
public class FunnelReport
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Days { get; set; }
    public List<FunnelStepStat> Steps { get; set; } = [];
}

public class FunnelStepStat
{
    public string Name { get; set; } = string.Empty;
    public int Users { get; set; }
    public double? PctOfPrevious { get; set; } // conversion from the previous step; null on step 0
    public double PctOfFirst { get; set; }     // conversion from the funnel entry
}
