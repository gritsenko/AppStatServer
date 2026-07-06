using AppStatServer;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer.Tests;

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

    [Test]
    public async Task Stats_report_totals_and_breakdowns()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", Level = "error", IsError = true, Release = "1.0.0", Timestamp = new DateTime(2024, 4, 18) },
            new AppEvent { Id = "e2", Level = "error", IsError = true, IsCrash = true, Release = "1.0.0", Timestamp = new DateTime(2024, 4, 18) },
            new AppEvent { Id = "e3", Level = "info", Release = "1.0.1", Timestamp = new DateTime(2024, 4, 19) },
        ]);
        await storage.SaveSessionsAsync([new AppSession { Id = "s1" }]);

        var stats = await storage.GetStatsAsync();

        await Assert.That(stats.TotalEvents).IsEqualTo(3);
        await Assert.That(stats.Errors).IsEqualTo(2);
        await Assert.That(stats.Crashes).IsEqualTo(1);
        await Assert.That(stats.TotalSessions).IsEqualTo(1);
        await Assert.That(stats.EventsByLevel.Single(l => l.Key == "error").Count).IsEqualTo(2);
        await Assert.That(stats.EventsByRelease.Single(r => r.Key == "1.0.0").Count).IsEqualTo(2);
        await Assert.That(stats.EventsPerDay.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Analytics_computes_users_sessions_and_distributions()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Level = "error", Release = "1.0.0", Os = "Android 13", DeviceModel = "Pixel 7", Timestamp = now.AddDays(-1) },
            new AppEvent { Id = "e2", UserId = "u1", Level = "info", Release = "1.0.0", Os = "Android 13", DeviceModel = "Pixel 7", Timestamp = now },
            new AppEvent { Id = "e3", UserId = "u2", Level = "info", Release = "1.0.1", Os = "iOS 17", DeviceModel = "iPhone14,2", Timestamp = now },
        ]);
        await storage.SaveSessionsAsync(
        [
            new AppSession { Id = "s1", DeviceId = "d1", Started = now.AddDays(-1), Duration = 5 },
            new AppSession { Id = "s2", DeviceId = "d2", Started = now, Duration = 120 },
        ]);

        var a = await storage.GetAnalyticsAsync(30);

        await Assert.That(a.Days).IsEqualTo(30);
        await Assert.That(a.UsersPerDay.Count).IsEqualTo(30);
        await Assert.That(a.Mau).IsEqualTo(2);
        await Assert.That(a.Dau).IsEqualTo(2);
        await Assert.That(a.NewUsers).IsEqualTo(2);
        await Assert.That(a.TotalSessions).IsEqualTo(2);
        await Assert.That(a.DurationBuckets.Count).IsEqualTo(6);
        await Assert.That(a.DurationBuckets.Single(b => b.Key == "0-10s").Count).IsEqualTo(1);
        await Assert.That(a.DurationBuckets.Single(b => b.Key == "1-30m").Count).IsEqualTo(1);
        await Assert.That(a.VersionDistribution.Single(v => v.Key == "1.0.0").Count).IsEqualTo(1);
        await Assert.That(a.OsDistribution.Count).IsEqualTo(2);
        await Assert.That(a.DeviceDistribution.Single(d => d.Key == "Pixel 7").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Event_groups_collapse_by_message_and_split_crashes()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Level = "error", Message = "Boom", Timestamp = new DateTime(2024, 1, 1) },
            new AppEvent { Id = "e2", UserId = "u2", Level = "error", Message = "Boom", Timestamp = new DateTime(2024, 1, 2) },
            new AppEvent { Id = "e3", UserId = "u1", Level = "info", Message = "Hi", Timestamp = new DateTime(2024, 1, 3) },
            new AppEvent { Id = "c1", UserId = "u1", Level = "fatal", Message = "Crash", IsCrash = true, Timestamp = new DateTime(2024, 1, 4) },
        ]);

        var eventGroups = await storage.GetEventGroupsAsync(crashesOnly: false);
        var crashGroups = await storage.GetEventGroupsAsync(crashesOnly: true);

        await Assert.That(eventGroups.Count).IsEqualTo(2); // Boom + Hi (crash excluded)
        var boom = eventGroups.Single(g => g.Title == "Boom");
        await Assert.That(boom.Count).IsEqualTo(2);
        await Assert.That(boom.Users).IsEqualTo(2);
        await Assert.That(boom.Sample.Id).IsEqualTo("e2"); // latest occurrence

        await Assert.That(crashGroups.Count).IsEqualTo(1);
        await Assert.That(crashGroups[0].Title).IsEqualTo("Crash");
        await Assert.That(crashGroups[0].IsCrash).IsTrue();
    }

    [Test]
    public async Task Event_groups_can_be_filtered_by_release_and_os()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Level = "error", Message = "Boom", Release = "1.0.0", Os = "Android 13" },
            new AppEvent { Id = "e2", UserId = "u2", Level = "error", Message = "Boom", Release = "1.0.1", Os = "iOS 17" },
        ]);

        var byRelease = await storage.GetEventGroupsAsync(crashesOnly: false, release: "1.0.0");
        await Assert.That(byRelease.Count).IsEqualTo(1);
        await Assert.That(byRelease[0].Users).IsEqualTo(1);

        var byOs = await storage.GetEventGroupsAsync(crashesOnly: false, os: "iOS 17");
        await Assert.That(byOs.Count).IsEqualTo(1);
        await Assert.That(byOs[0].Sample.Id).IsEqualTo("e2");
    }

    [Test]
    public async Task Facets_report_distinct_releases_and_oses()
    {
        using var storage = NewInMemoryStorage();

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", Release = "1.0.0", Os = "Android 13" },
            new AppEvent { Id = "e2", Release = "1.0.1", Os = "Android 13" },
            new AppEvent { Id = "e3", Release = "1.0.0", Os = "iOS 17" },
        ]);

        var facets = await storage.GetFacetsAsync();

        await Assert.That(facets.Releases).IsEquivalentTo(new[] { "1.0.0", "1.0.1" });
        await Assert.That(facets.Oses).IsEquivalentTo(new[] { "Android 13", "iOS 17" });
    }
}
