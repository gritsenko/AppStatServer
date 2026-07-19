using AppStatServer;
using AppStatServer.Data;
using LiteDB;

namespace AppStatServer.Tests;

// Retention and funnel math over the storage layer. Both features anchor their windows on
// local "now" (like the rest of the analytics), so test data is placed relative to today.
public class RetentionAndFunnelTests
{
    private static LiteDbEventStorage NewInMemoryStorage() =>
        new(new LiteDatabase(new MemoryStream()));

    private static TrackEvent Track(string user, string name, DateTime ts) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        UserId = user,
        Timestamp = ts,
    };

    // Monday of the current week — the cohort anchor used by GetRetentionAsync.
    private static DateTime ThisWeek()
    {
        var today = DateTime.Now.Date;
        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    [Test]
    public async Task Day1_retention_counts_users_active_the_day_after_first_seen()
    {
        using var storage = NewInMemoryStorage();
        var today = DateTime.Now.Date;

        await storage.SaveTrackEventsAsync(
        [
            // u1: first seen 2 days ago, came back the next day -> D1 retained.
            Track("u1", "started", today.AddDays(-2)),
            Track("u1", "started", today.AddDays(-1)),
            // u2: first seen 2 days ago, never returned -> D1 churned.
            Track("u2", "started", today.AddDays(-2)),
        ]);

        var r = await storage.GetRetentionAsync(8);

        await Assert.That(r.D1.Eligible).IsEqualTo(2);
        await Assert.That(r.D1.Retained).IsEqualTo(1);
        await Assert.That(r.D1.Pct).IsEqualTo(50.0);
        // Nobody is 7 days old yet, so D7 can't be measured.
        await Assert.That(r.D7.Eligible).IsEqualTo(0);
        await Assert.That(r.D7.Pct).IsNull();
    }

    [Test]
    public async Task Weekly_cohort_tracks_the_share_of_users_who_come_back()
    {
        using var storage = NewInMemoryStorage();
        var cohortWeek = ThisWeek().AddDays(-14);

        await storage.SaveTrackEventsAsync(
        [
            // Both users first appear in the cohort week; only u1 returns two weeks later.
            Track("u1", "started", cohortWeek),
            Track("u2", "started", cohortWeek.AddDays(1)),
            Track("u1", "started", ThisWeek()),
        ]);

        var r = await storage.GetRetentionAsync(8);
        var cohort = r.Cohorts.Single(c => c.Week == cohortWeek.ToString("yyyy-MM-dd"));

        await Assert.That(cohort.Size).IsEqualTo(2);
        await Assert.That(cohort.Values[0]).IsEqualTo(100.0); // first week: active by definition
        await Assert.That(cohort.Values[1]).IsEqualTo(0.0);   // nobody came back in week 1
        await Assert.That(cohort.Values[2]).IsEqualTo(50.0);  // u1 of 2 returned in week 2
    }

    [Test]
    public async Task Funnels_can_be_saved_listed_and_deleted()
    {
        using var storage = NewInMemoryStorage();

        var saved = await storage.SaveFunnelAsync(new Funnel { Name = "Purchase", Steps = ["visit", "buy"] });
        var funnels = await storage.GetFunnelsAsync();

        await Assert.That(saved.Id).IsNotEmpty();
        await Assert.That(funnels.Count).IsEqualTo(1);
        await Assert.That(funnels[0].Name).IsEqualTo("Purchase");

        await Assert.That(await storage.DeleteFunnelAsync(saved.Id)).IsTrue();
        await Assert.That((await storage.GetFunnelsAsync()).Count).IsEqualTo(0);
        await Assert.That(await storage.DeleteFunnelAsync("missing")).IsFalse();
    }

    [Test]
    public async Task Funnel_report_only_counts_steps_passed_in_order()
    {
        using var storage = NewInMemoryStorage();
        var now = DateTime.Now;

        await storage.SaveTrackEventsAsync(
        [
            // u1 converts: A then B.
            Track("u1", "A", now.AddHours(-3)),
            Track("u1", "B", now.AddHours(-2)),
            // u2 has both events but in the wrong order — B must not count.
            Track("u2", "B", now.AddHours(-3)),
            Track("u2", "A", now.AddHours(-2)),
            // u3 only entered the funnel.
            Track("u3", "A", now.AddHours(-1)),
        ]);

        var funnel = await storage.SaveFunnelAsync(new Funnel { Name = "test", Steps = ["A", "B"] });
        var report = await storage.GetFunnelReportAsync(funnel.Id, 30);

        await Assert.That(report).IsNotNull();
        await Assert.That(report!.Steps[0].Users).IsEqualTo(3);
        await Assert.That(report.Steps[1].Users).IsEqualTo(1);
        await Assert.That(report.Steps[0].PctOfPrevious).IsNull();
        await Assert.That(report.Steps[1].PctOfPrevious).IsEqualTo(33.3);
        await Assert.That(report.Steps[1].PctOfFirst).IsEqualTo(33.3);
    }

    [Test]
    public async Task Funnel_report_for_unknown_id_is_null()
    {
        using var storage = NewInMemoryStorage();

        await Assert.That(await storage.GetFunnelReportAsync("missing", 30)).IsNull();
    }
}
