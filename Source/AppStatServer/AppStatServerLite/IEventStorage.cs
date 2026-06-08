using AppStatServerLite.Data;
using System.Collections.Immutable;

namespace AppStatServerLite;

public interface IEventStorage
{
    Task SaveEventsAsync(IEnumerable<AppEvent> appEvents);
    Task<ImmutableList<AppEvent>> GetRecentEventsAsync();

    Task SaveSessionsAsync(IEnumerable<AppSession> sessions);
    Task<ImmutableList<AppSession>> GetRecentSessionsAsync();
}
