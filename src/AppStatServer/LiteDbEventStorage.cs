using System.Collections.Immutable;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer;

public class LiteDbEventStorage : IEventStorage, IDisposable
{
    private readonly LiteDatabase _db;

    public LiteDbEventStorage(string? dbFileName = "AppStat.db")
        : this(new LiteDatabase(dbFileName ?? "AppStat.db"))
    {
    }

    // Allows injecting an in-memory LiteDatabase (e.g. new LiteDatabase(new MemoryStream())) for tests.
    public LiteDbEventStorage(LiteDatabase db)
    {
        _db = db;
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

    public Task SaveSessionsAsync(IEnumerable<AppSession> sessions)
    {
        var col = _db.GetCollection<AppSession>("sessions");
        // Sessions are sent multiple times per session id (init -> update -> end),
        // so upsert to keep the latest state instead of throwing on duplicate ids.
        col.Upsert(sessions);
        return Task.CompletedTask;
    }

    public Task<ImmutableList<AppSession>> GetRecentSessionsAsync()
    {
        var col = _db.GetCollection<AppSession>("sessions");
        var result = col.Find(x => true, 0, 100);
        return Task.FromResult(result.ToImmutableList());
    }

    public void Dispose() => _db.Dispose();
}
