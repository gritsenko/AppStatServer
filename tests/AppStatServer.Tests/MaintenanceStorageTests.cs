using AppStatServer;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer.Tests;

// The Maintenance page's data-retention actions: purging old raw records and compacting.
public class MaintenanceStorageTests
{
    private static LiteDbEventStorage NewInMemoryStorage() =>
        new(new LiteDatabase(new MemoryStream()));

    [Test]
    public async Task Purge_deletes_only_records_older_than_the_cutoff()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "old", Timestamp = now.AddDays(-100) },
            new AppEvent { Id = "new", Timestamp = now.AddDays(-1) },
        ]);
        await storage.SaveSessionsAsync(
        [
            new AppSession { Id = "s-old", Started = now.AddDays(-100) },
            new AppSession { Id = "s-new", Started = now.AddDays(-1) },
        ]);
        await storage.SaveTrackEventsAsync(
        [
            new TrackEvent { Id = "t-old", Name = "x", UserId = "u", Timestamp = now.AddDays(-100) },
            new TrackEvent { Id = "t-new", Name = "x", UserId = "u", Timestamp = now.AddDays(-1) },
        ]);

        // The preview must report the same counts without deleting anything.
        var estimate = await storage.EstimatePurgeAsync(30);
        await Assert.That(estimate.Total).IsEqualTo(3);
        await Assert.That(estimate.Bytes).IsGreaterThan(0);
        await Assert.That((await storage.GetRecentEventsAsync()).Count).IsEqualTo(2);

        var result = await storage.PurgeAsync(30);

        await Assert.That(result.Events).IsEqualTo(1);
        await Assert.That(result.Sessions).IsEqualTo(1);
        await Assert.That(result.TrackEvents).IsEqualTo(1);
        await Assert.That(result.Total).IsEqualTo(3);
        await Assert.That(result.Bytes).IsEqualTo(estimate.Bytes);

        await Assert.That((await storage.GetRecentEventsAsync()).Single().Id).IsEqualTo("new");
        await Assert.That((await storage.GetRecentSessionsAsync()).Single().Id).IsEqualTo("s-new");
        await Assert.That((await storage.GetRecentTrackEventsAsync()).Single().Id).IsEqualTo("t-new");
    }

    [Test]
    public async Task Purge_on_fresh_data_removes_nothing()
    {
        using var storage = NewInMemoryStorage();
        await storage.SaveEventsAsync([new AppEvent { Id = "e", Timestamp = DateTime.Now }]);

        var result = await storage.PurgeAsync(30);

        await Assert.That(result.Total).IsEqualTo(0);
        await Assert.That((await storage.GetRecentEventsAsync()).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Compact_survives_the_in_memory_database()
    {
        using var storage = NewInMemoryStorage();
        await storage.SaveEventsAsync([new AppEvent { Id = "e", Timestamp = DateTime.Now }]);

        // No file to shrink in memory — the call must still succeed and report zero sizes.
        var result = await storage.CompactAsync();

        await Assert.That(result.BytesBefore).IsEqualTo(0);
        await Assert.That(result.BytesAfter).IsEqualTo(0);
        await Assert.That((await storage.GetRecentEventsAsync()).Count).IsEqualTo(1);
    }
}
