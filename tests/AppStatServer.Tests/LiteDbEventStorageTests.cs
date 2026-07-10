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
    public async Task Diagnostics_split_crashes_from_handled_errors()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            // Handled error (exception, but no thread crashed).
            new AppEvent { Id = "e1", UserId = "u1", Level = "error", Message = "Boom", IsError = true, Release = "1.0.0", Timestamp = now.AddDays(-1) },
            new AppEvent { Id = "e2", UserId = "u2", Level = "error", Message = "Boom", IsError = true, Release = "1.0.0", Timestamp = now },
            // Crash (thread crashed). A crash usually also carries the exception flag.
            new AppEvent { Id = "c1", UserId = "u1", Level = "fatal", Message = "Fatal", IsError = true, IsCrash = true, Release = "1.0.0", Timestamp = now },
            // Plain info log: neither a crash nor an error, must be excluded.
            new AppEvent { Id = "i1", UserId = "u3", Level = "info", Message = "Hello", Release = "1.0.0", Timestamp = now },
        ]);

        var report = await storage.GetDiagnosticsAsync(30);

        await Assert.That(report.TotalCrashes).IsEqualTo(1);
        await Assert.That(report.TotalErrors).IsEqualTo(2);
        await Assert.That(report.AffectedUsers).IsEqualTo(2); // u1, u2 (u3 only logged info)
        await Assert.That(report.Groups.Count).IsEqualTo(2);  // one crash group + one error group

        var crash = report.Groups.Single(g => g.Kind == "crash");
        await Assert.That(crash.Title).IsEqualTo("Fatal");
        await Assert.That(crash.Resolved).IsFalse();

        var error = report.Groups.Single(g => g.Kind == "error");
        await Assert.That(error.Title).IsEqualTo("Boom");
        await Assert.That(error.Count).IsEqualTo(2);
        await Assert.That(error.Users).IsEqualTo(2);

        await Assert.That(report.CrashesPerDay.Count).IsEqualTo(30);
        await Assert.That(report.CrashesPerDay.Sum(d => d.Count)).IsEqualTo(1);
        await Assert.That(report.ErrorsPerDay.Sum(d => d.Count)).IsEqualTo(2);
    }

    [Test]
    public async Task Diagnostics_can_be_filtered_by_release()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Message = "Boom", IsError = true, Release = "1.0.0", Timestamp = now },
            new AppEvent { Id = "e2", UserId = "u2", Message = "Boom", IsError = true, Release = "1.0.1", Timestamp = now },
        ]);

        var report = await storage.GetDiagnosticsAsync(30, release: "1.0.0");

        await Assert.That(report.TotalErrors).IsEqualTo(1);
        await Assert.That(report.Groups.Single().Release).IsEqualTo("1.0.0");
    }

    [Test]
    public async Task Resolving_a_group_marks_it_resolved_and_reopening_clears_it()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Message = "Boom", IsError = true, Release = "1.0.0", Timestamp = now.AddMinutes(-5) },
        ]);

        var before = await storage.GetDiagnosticsAsync(30);
        var key = before.Groups.Single().Key;
        await Assert.That(before.Groups.Single().Resolved).IsFalse();

        await storage.SetResolutionAsync(key, true);

        var afterResolve = await storage.GetDiagnosticsAsync(30);
        await Assert.That(afterResolve.Groups.Single().Resolved).IsTrue();
        await Assert.That(afterResolve.OpenGroups).IsEqualTo(0);

        await storage.SetResolutionAsync(key, false);

        var afterReopen = await storage.GetDiagnosticsAsync(30);
        await Assert.That(afterReopen.Groups.Single().Resolved).IsFalse();
        await Assert.That(afterReopen.OpenGroups).IsEqualTo(1);
    }

    [Test]
    public async Task Resolved_group_reopens_when_it_recurs()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e1", UserId = "u1", Message = "Boom", IsError = true, Timestamp = now.AddDays(-2) },
        ]);

        var key = (await storage.GetDiagnosticsAsync(30)).Groups.Single().Key;
        await storage.SetResolutionAsync(key, true);
        await Assert.That((await storage.GetDiagnosticsAsync(30)).Groups.Single().Resolved).IsTrue();

        // The same signature happens again after it was resolved -> it must reopen.
        await storage.SaveEventsAsync(
        [
            new AppEvent { Id = "e2", UserId = "u2", Message = "Boom", IsError = true, Timestamp = now.AddMinutes(5) },
        ]);

        var after = await storage.GetDiagnosticsAsync(30);
        await Assert.That(after.Groups.Single().Resolved).IsFalse();
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
