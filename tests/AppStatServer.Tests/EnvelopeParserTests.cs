using AppStatServer.Sentry;

namespace AppStatServer.Tests;

public class EnvelopeParserTests
{
    // A realistic event envelope: header line + section marker + event payload.
    private const string EventEnvelope =
        """{"sdk":{"name":"sentry.dotnet","version":"4.13.0"},"event_id":"abc123","sent_at":"2024-04-18T20:00:01Z"}""" + "\n" +
        """{"type":"event","length":42}""" + "\n" +
        """{"event_id":"abc123","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"myapp@1.2.3","exception":{"values":[{"type":"System.Exception","value":"boom"}]},"threads":{"values":[{"id":1,"crashed":true,"stacktrace":{"frames":[{"function":"Outer","filename":"Outer.cs","lineno":5,"in_app":true},{"function":"Inner","filename":"Inner.cs","lineno":10,"in_app":true}]}}]},"contexts":{"trace":{"span_id":"span1","trace_id":"trace1"},"os":{"raw_description":"Windows 11"},"device":{"model":"iPhone14,2","family":"iPhone"}},"user":{"id":"user-1"}}""";

    private const string SessionEnvelope =
        """{"sid":"session-1","did":"device-1","init":true,"started":"2024-04-18T20:00:00Z","timestamp":"2024-04-18T20:05:00Z","seq":2,"duration":300,"errors":1,"attrs":{"release":"myapp@1.2.3","environment":"production"}}""";

    [Test]
    public async Task Parses_event_payload_into_app_event()
    {
        var parsed = EnvelopeParser.Parse(EventEnvelope);

        await Assert.That(parsed.Events.Count).IsEqualTo(1);

        var ev = parsed.Events[0];
        await Assert.That(ev.Id).IsEqualTo("abc123");
        await Assert.That(ev.Message).IsEqualTo("boom");
        await Assert.That(ev.Level).IsEqualTo("error");
        await Assert.That(ev.Release).IsEqualTo("1.2.3");
        await Assert.That(ev.IsError).IsTrue();
        await Assert.That(ev.IsCrash).IsTrue();
        await Assert.That(ev.TraceId).IsEqualTo("trace1");
        await Assert.That(ev.SpanId).IsEqualTo("span1");
        await Assert.That(ev.Os).IsEqualTo("Windows 11");
        await Assert.That(ev.DeviceModel).IsEqualTo("iPhone14,2");
        await Assert.That(ev.UserId).IsEqualTo("user-1");
        await Assert.That(parsed.LastId).IsEqualTo("abc123");
    }

    [Test]
    public async Task Captures_stacktrace_and_raw_payload()
    {
        var ev = EnvelopeParser.Parse(EventEnvelope).Events[0];

        await Assert.That(ev.StackTrace).IsNotNull();
        await Assert.That(ev.StackTrace!).Contains("System.Exception: boom");
        await Assert.That(ev.StackTrace!).Contains("Inner.cs:line 10");
        await Assert.That(ev.StackTrace!).Contains("Outer.cs:line 5");
        // Raw event entry is persisted (was previously dropped as "").
        await Assert.That(ev.EventEntry).IsNotNull();
        await Assert.That(ev.EventEntry!).Contains("\"event_id\":\"abc123\"");
    }

    [Test]
    public async Task Parses_session_payload_into_app_session()
    {
        var parsed = EnvelopeParser.Parse(SessionEnvelope);

        await Assert.That(parsed.Sessions.Count).IsEqualTo(1);

        var session = parsed.Sessions[0];
        await Assert.That(session.Id).IsEqualTo("session-1");
        await Assert.That(session.DeviceId).IsEqualTo("device-1");
        await Assert.That(session.Init).IsTrue();
        await Assert.That(session.Seq).IsEqualTo(2);
        await Assert.That(session.Duration).IsEqualTo(300);
        await Assert.That(session.Errors).IsEqualTo(1);
        await Assert.That(session.Release).IsEqualTo("myapp@1.2.3");
        await Assert.That(session.Environment).IsEqualTo("production");
    }

    [Test]
    public async Task Links_event_to_session_in_same_envelope()
    {
        var parsed = EnvelopeParser.Parse(SessionEnvelope + "\n" + EventEnvelope);

        await Assert.That(parsed.Events.Count).IsEqualTo(1);
        await Assert.That(parsed.Events[0].SessionId).IsEqualTo("session-1");
    }

    [Test]
    public async Task Event_without_release_does_not_throw()
    {
        const string noReleaseEvent =
            """{"event_id":"no-release","timestamp":"2024-04-18T20:00:00Z","level":"info","logentry":{"message":"hello"}}""";

        var parsed = EnvelopeParser.Parse(noReleaseEvent);

        await Assert.That(parsed.Events.Count).IsEqualTo(1);
        await Assert.That(parsed.Events[0].Message).IsEqualTo("hello");
        await Assert.That(parsed.Events[0].Release).IsEqualTo(string.Empty);
        await Assert.That(parsed.Events[0].StackTrace).IsNull();
    }

    [Test]
    public async Task Event_with_blank_event_id_gets_a_generated_id()
    {
        // A blank id must not reach storage as "" — LiteDB would try to auto-generate an
        // ObjectId for it and then fail casting back to the string Id property.
        const string blankIdEvent =
            """{"event_id":"","timestamp":"2024-04-18T20:00:00Z","level":"info","logentry":{"message":"hello"}}""";

        var parsed = EnvelopeParser.Parse(blankIdEvent);

        await Assert.That(parsed.Events.Count).IsEqualTo(1);
        await Assert.That(parsed.Events[0].Id).IsNotNull();
        await Assert.That(parsed.Events[0].Id).IsNotEmpty();
        // The response id echoes the id we actually stored, never a blank string.
        await Assert.That(parsed.LastId).IsEqualTo(parsed.Events[0].Id);
    }

    [Test]
    public async Task Session_with_blank_sid_is_skipped()
    {
        // sid is the upsert key; a blank one can't be a key and would blow up on persist.
        const string blankSidSession =
            """{"sid":"","did":"device-1","init":true,"started":"2024-04-18T20:00:00Z","timestamp":"2024-04-18T20:05:00Z","seq":1,"duration":60,"errors":0,"attrs":{"release":"myapp@1.2.3","environment":"production"}}""";

        var parsed = EnvelopeParser.Parse(blankSidSession);

        await Assert.That(parsed.Sessions).IsEmpty();
    }

    [Test]
    public async Task Empty_body_yields_no_records()
    {
        var parsed = EnvelopeParser.Parse("");

        await Assert.That(parsed.Events).IsEmpty();
        await Assert.That(parsed.Sessions).IsEmpty();
        await Assert.That(parsed.LastId).IsEqualTo("0");
    }
}
