using AppStatServerLite;
using AppStatServerLite.Data;
using LiteDB;

namespace AppStatServerLite.Tests;

public class LiteDbEventStorageTests
{
    private static LiteDbEventStorage NewInMemoryStorage() =>
        new(new LiteDatabase(new MemoryStream()));

    [Test]
    public async Task Saves_and_reads_back_events()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", Message = "first", Level = "error" },
            new AppEvent { Id = "e2", Message = "second", Level = "info" },
        ]);

        var events = await storage.GetRecentEventsAsync();

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events.Any(e => e.Id == "e1" && e.Message == "first")).IsTrue();
        await Assert.That(events.Any(e => e.Id == "e2" && e.Message == "second")).IsTrue();
    }

    [Test]
    public async Task Saves_and_reads_back_sessions()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveSessionsAsync([new AppSession { Id = "s1", Errors = 0, Release = "1.0.0" }]);

        var sessions = await storage.GetRecentSessionsAsync();

        await Assert.That(sessions.Count).IsEqualTo(1);
        await Assert.That(sessions[0].Release).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task Sessions_are_upserted_by_id()
    {
        using var storage = NewInMemoryStorage();

        // Same session id sent twice (init -> end); the latest state must win, not duplicate.
        await storage.SaveSessionsAsync([new AppSession { Id = "s1", Errors = 0, Seq = 1 }]);
        await storage.SaveSessionsAsync([new AppSession { Id = "s1", Errors = 3, Seq = 2 }]);

        var sessions = await storage.GetRecentSessionsAsync();

        await Assert.That(sessions.Count).IsEqualTo(1);
        await Assert.That(sessions[0].Errors).IsEqualTo(3);
        await Assert.That(sessions[0].Seq).IsEqualTo(2);
    }
}
