using AppStatServer.Data;
using System.Collections.Immutable;

namespace AppStatServer;

public interface IEventStorage
{
    Task SaveEventsAsync(IEnumerable<AppEvent> appEvents);
    Task<ImmutableList<AppEvent>> GetRecentEventsAsync();

    Task SaveSessionsAsync(IEnumerable<AppSession> sessions);
    Task<ImmutableList<AppSession>> GetRecentSessionsAsync();
}
