using AppStatServer.Data;
using System.Collections.Immutable;

namespace AppStatServer;

public interface IEventStorage
{
    Task SaveEventsAsync(IEnumerable<AppEvent> appEvents);
    Task<ImmutableList<AppEvent>> GetRecentEventsAsync();

    Task SaveSessionsAsync(IEnumerable<AppSession> sessions);
    Task<ImmutableList<AppSession>> GetRecentSessionsAsync();

    Task SaveTrackEventsAsync(IEnumerable<TrackEvent> trackEvents);
    Task<ImmutableList<TrackEvent>> GetRecentTrackEventsAsync();

    Task<DashboardStats> GetStatsAsync();

    Task<AnalyticsData> GetAnalyticsAsync(int days);

    // crashesOnly=true groups only crashes; false groups all non-crash events.
    // Optional release/os narrow the events before grouping.
    Task<ImmutableList<EventGroup>> GetEventGroupsAsync(bool crashesOnly, string? release = null, string? os = null);

    Task<Facets> GetFacetsAsync();

    // Optional release/os narrow the events before rolling them up.
    Task<EventReport> GetEventReportAsync(int days, string? release = null, string? os = null);

    // Combined crashes + handled-errors report over a rolling window, optionally narrowed
    // to one app version. Carries the two per-day series and the grouped signatures.
    Task<DiagnosticsReport> GetDiagnosticsAsync(int days, string? release = null);

    // Mark a crash/error group (by its DiagnosticGroup.Key) resolved or reopen it.
    Task<bool> SetResolutionAsync(string key, bool resolved);
}
