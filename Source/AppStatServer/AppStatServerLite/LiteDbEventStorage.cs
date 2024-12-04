using System.Collections.Immutable;
using AppStatServerLite.Data;
using LiteDB;

namespace AppStatServerLite;

public class LiteDbEventStorage : IEventStorage, IDisposable
{
    private readonly string? _dbFileName;
    private readonly LiteDatabase _db;

    public LiteDbEventStorage(string? dbFileName = "AppStat.db")
    {
        _dbFileName = dbFileName;
        _db = new LiteDatabase(_dbFileName);
    }

    public Task SaveEventsAsync(IEnumerable<AppEvent> appEvents)
    {
        var col = _db.GetCollection<AppEvent>("events");
        col.Insert(appEvents);
        return Task.CompletedTask;
    }

    public Task<ImmutableList<AppEvent>> GetRecentEventsAsync()
    {
        var col = _db.GetCollection<AppEvent>("events");
        var result = col.Find(x => true, 0, 100);
        return Task.FromResult(result.ToImmutableList());
    }

    public void Dispose() => _db.Dispose();
}