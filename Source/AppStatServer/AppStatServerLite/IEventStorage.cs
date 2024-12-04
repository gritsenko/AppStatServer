using AppStatServerLite.Data;
using System.Collections.Immutable;

namespace AppStatServerLite;

public interface IEventStorage
{
    Task SaveEventsAsync(IEnumerable<AppEvent> appEvent);
    Task<ImmutableList<AppEvent>> GetRecentEventsAsync();
}