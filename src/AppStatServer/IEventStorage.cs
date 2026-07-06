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

    Task<EventReport> GetEventReportAsync(int days);
}
