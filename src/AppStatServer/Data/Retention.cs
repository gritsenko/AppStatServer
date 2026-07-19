namespace AppStatServer.Data;

// Cohort retention: users grouped by the week of their first-ever activity, then for each
// later week the share of that cohort that came back. Plus the classic day-N return rates
// (D1/D7/D30) computed over exact days, AppCenter/Amplitude style.
public class RetentionData
{
    public int Weeks { get; set; }

    public RetentionPoint D1 { get; set; } = new();
    public RetentionPoint D7 { get; set; } = new();
    public RetentionPoint D30 { get; set; } = new();

    public List<CohortRow> Cohorts { get; set; } = [];
}

// One day-N retention figure. Eligible = users whose first day is at least N days in the
// past (younger users can't be measured yet); Retained = of those, active on day first+N.
public class RetentionPoint
{
    public int Eligible { get; set; }
    public int Retained { get; set; }
    public double? Pct { get; set; } // null when nobody is eligible yet
}

// One weekly cohort: who showed up first that week, and the % still active k weeks later.
public class CohortRow
{
    public string Week { get; set; } = string.Empty; // cohort week start (Monday), yyyy-MM-dd
    public int Size { get; set; }

    // Index k = share (%) of the cohort active during week k after their first week.
    // k=0 is 100 by construction; null marks weeks that are still in the future.
    public List<double?> Values { get; set; } = [];
}
