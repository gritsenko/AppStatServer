using AppStatServer.Sentry;

namespace AppStatServer.Tests;

public class EnvelopeParserTests
{
    // A realistic event envelope: header line + section marker + event payload.
    private const string EventEnvelope =
        """{"sdk":{"name":"sentry.dotnet","version":"4.13.0"},"event_id":"abc123","sent_at":"2024-04-18T20:00:01Z"}""" + "\n" +
        """{"type":"event","length":42}""" + "\n" +
        """{"event_id":"abc123","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"myapp@1.2.3","exception":{"values":[{"type":"System.Exception","value":"boom"}]},"threads":{"values":[{"id":1,"crashed":true,"stacktrace":{"frames":[{"function":"Outer","filename":"Outer.cs","lineno":5,"in_app":true},{"function":"Inner","filename":"Inner.cs","lineno":10,"in_app":true}]}}]},"contexts":{"trace":{"span_id":"span1","trace_id":"trace1"},"os":{"raw_description":"Windows 11"},"device":{"model":"iPhone14,2","family":"iPhone","arch":"arm64"}},"user":{"id":"user-1"}}""";

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
        await Assert.That(ev.Arch).IsEqualTo("arm64");
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

    // The Sentry .NET SDK attaches managed frames to the exception entry itself (no thread block),
    // which is what CaptureException produces. These were previously dropped.
    private const string ExceptionFramesEnvelope =
        """{"event_id":"exc-1","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"myapp@1.2.3","exception":{"values":[{"type":"System.Exception","value":"boom","stacktrace":{"frames":[{"function":"Outer","filename":"Outer.cs","lineno":5,"in_app":true},{"function":"Inner","filename":"Inner.cs","lineno":10,"in_app":true}]}}]}}""";

    // A trimmed/AOT-style frame-less exception: no frames anywhere, but the app attaches the
    // capture-site stack as an extra. This is the "стектрейсы никакие" case.
    private const string FramelessWithExtraEnvelope =
        """{"event_id":"frameless-1","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"myapp@1.2.3","exception":{"values":[{"type":"System.IndexOutOfRangeException","value":"Index was outside the bounds of the array."}]},"extra":{"stack_trace_text":"(exception carried no stack — capture-site trace below)\n   at Pix2d.Foo.Bar()","exception_chain":"[0] System.IndexOutOfRangeException","app_context":"plat=Android tool=Brush","last_command":"Undo"}}""";

    [Test]
    public async Task Captures_exception_level_frames()
    {
        var ev = EnvelopeParser.Parse(ExceptionFramesEnvelope).Events[0];

        await Assert.That(ev.StackTrace).IsNotNull();
        await Assert.That(ev.StackTrace!).Contains("System.Exception: boom");
        await Assert.That(ev.StackTrace!).Contains("Inner.cs:line 10");
        await Assert.That(ev.StackTrace!).Contains("Outer.cs:line 5");
    }

    [Test]
    public async Task Frameless_exception_falls_back_to_stack_trace_text_extra()
    {
        var ev = EnvelopeParser.Parse(FramelessWithExtraEnvelope).Events[0];

        await Assert.That(ev.StackTrace).IsNotNull();
        await Assert.That(ev.StackTrace!).Contains("System.IndexOutOfRangeException");
        await Assert.That(ev.StackTrace!).Contains("capture-site fallback");
        await Assert.That(ev.StackTrace!).Contains("at Pix2d.Foo.Bar()");
    }

    [Test]
    public async Task Extract_extras_returns_app_context_fields()
    {
        var extras = EnvelopeParser.ExtractExtras(FramelessWithExtraEnvelope);

        await Assert.That(extras["app_context"]).IsEqualTo("plat=Android tool=Brush");
        await Assert.That(extras["last_command"]).IsEqualTo("Undo");
        await Assert.That(extras.ContainsKey("exception_chain")).IsTrue();
    }

    // Senders that hand-build the payload have no device context; the architecture then arrives
    // as an extra (or as the free-form CPU description).
    private const string CpuDescriptionEnvelope =
        """{"event_id":"cpu-1","timestamp":"2024-04-18T20:00:00Z","level":"error","exception":{"values":[{"type":"System.Exception","value":"boom"}]},"contexts":{"device":{"cpu_description":" x86_64 "}}}""";

    private const string ArchExtraEnvelope =
        """{"event_id":"cpu-2","timestamp":"2024-04-18T20:00:00Z","level":"error","exception":{"values":[{"type":"System.Exception","value":"boom"}]},"extra":{"architecture":"Arm64"}}""";

    [Test]
    public async Task Arch_falls_back_to_cpu_description_then_extras()
    {
        var fromCpu = EnvelopeParser.Parse(CpuDescriptionEnvelope).Events[0];
        var fromExtra = EnvelopeParser.Parse(ArchExtraEnvelope).Events[0];

        await Assert.That(fromCpu.Arch).IsEqualTo("x86_64");
        await Assert.That(fromExtra.Arch).IsEqualTo("Arm64");
    }

    [Test]
    public async Task Extract_arch_from_raw_reads_already_stored_payloads()
    {
        // Events ingested before AppEvent.Arch existed still carry it in the raw payload.
        await Assert.That(EnvelopeParser.ExtractArchFromRaw(EventEnvelope.Split('\n')[2])).IsEqualTo("arm64");
        await Assert.That(EnvelopeParser.ExtractArchFromRaw(null)).IsNull();
        await Assert.That(EnvelopeParser.ExtractArchFromRaw("not json")).IsNull();
        // Present but architecture-less payload — no invented value.
        await Assert.That(EnvelopeParser.ExtractArchFromRaw(ExceptionFramesEnvelope)).IsNull();
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
        // Session releases get the same "app@1.2.3" -> "1.2.3" normalisation as events.
        await Assert.That(session.Release).IsEqualTo("1.2.3");
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

    // An ANR recovered from the OS exit record on the next launch: a bare message event with no
    // exception and no thread list, identified only by its level and tags. Filed as a plain log
    // before the classifier learned to read those, which is why ANRs never reached diagnostics.
    private const string RecoveredAnrEnvelope =
        """{"event_id":"anr-1","timestamp":"2024-04-18T20:00:00Z","level":"fatal","release":"pix2d@3.13.0","logentry":{"message":"ANR in com.pix2d.pix2dapp.MainActivity.onCreate"},"tags":{"crash_recovered":"true","crash_source":"ProcessExit:Anr","signal":"ANR","last_command":"SaveProject"},"extra":{"exit_trace":"\"main\" prio=5 tid=1 Blocked at java.lang.Object.wait(Native method)"}}""";

    // The Android SDK's own ANR detector sends a real exception instead, carrying an ANR mechanism.
    private const string SdkAnrEnvelope =
        """{"event_id":"anr-2","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"pix2d@3.13.0","exception":{"values":[{"type":"ApplicationNotResponding","value":"Application Not Responding for at least 5000 ms.","mechanism":{"type":"ANR","handled":false}}]}}""";

    // A recovered native crash — same message-only shape as an ANR, but not an ANR.
    private const string RecoveredNativeCrashEnvelope =
        """{"event_id":"native-1","timestamp":"2024-04-18T20:00:00Z","level":"fatal","release":"pix2d@3.13.0","logentry":{"message":"Native crash SIGSEGV in libSkiaSharp.so: sk_surface_draw"},"tags":{"crash_recovered":"true","crash_source":"ProcessExit:CrashNative","signal":"SIGSEGV"}}""";

    // A handled error: an exception the app reported and kept running from.
    private const string HandledErrorEnvelope =
        """{"event_id":"handled-1","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"pix2d@3.13.0","exception":{"values":[{"type":"System.IO.IOException","value":"Access to the path is denied.","mechanism":{"type":"AppDomain","handled":true}}]},"tags":{"handled":"true"}}""";

    [Test]
    public async Task Recovered_anr_is_classified_as_a_crash_and_an_anr()
    {
        var ev = EnvelopeParser.Parse(RecoveredAnrEnvelope).Events[0];

        await Assert.That(ev.Message).IsEqualTo("ANR in com.pix2d.pix2dapp.MainActivity.onCreate");
        await Assert.That(ev.IsAnr).IsTrue();
        // It has no exception and no crashed thread, but the app still died — it must reach
        // diagnostics rather than the plain event log.
        await Assert.That(ev.IsCrash).IsTrue();
        await Assert.That(ev.IsError).IsTrue();
    }

    [Test]
    public async Task Sdk_reported_anr_exception_is_classified_as_an_anr()
    {
        var ev = EnvelopeParser.Parse(SdkAnrEnvelope).Events[0];

        await Assert.That(ev.IsAnr).IsTrue();
        // mechanism.handled == false, even though the level is only "error".
        await Assert.That(ev.IsCrash).IsTrue();
    }

    [Test]
    public async Task Recovered_native_crash_is_a_crash_but_not_an_anr()
    {
        var ev = EnvelopeParser.Parse(RecoveredNativeCrashEnvelope).Events[0];

        await Assert.That(ev.IsCrash).IsTrue();
        await Assert.That(ev.IsError).IsTrue();
        await Assert.That(ev.IsAnr).IsFalse();
    }

    [Test]
    public async Task Handled_exception_stays_an_error()
    {
        var ev = EnvelopeParser.Parse(HandledErrorEnvelope).Events[0];

        await Assert.That(ev.IsError).IsTrue();
        await Assert.That(ev.IsCrash).IsFalse();
        await Assert.That(ev.IsAnr).IsFalse();
    }

    [Test]
    public async Task Plain_log_message_is_neither_crash_nor_error()
    {
        const string logEnvelope =
            """{"event_id":"log-1","timestamp":"2024-04-18T20:00:00Z","level":"info","logentry":{"message":"Project opened"}}""";

        var ev = EnvelopeParser.Parse(logEnvelope).Events[0];

        await Assert.That(ev.IsCrash).IsFalse();
        await Assert.That(ev.IsError).IsFalse();
        await Assert.That(ev.IsAnr).IsFalse();
    }

    [Test]
    public async Task Tags_are_extracted_from_the_raw_payload()
    {
        var tags = EnvelopeParser.ExtractTags(EnvelopeParser.Parse(RecoveredAnrEnvelope).Events[0].EventEntry);

        await Assert.That(tags["signal"]).IsEqualTo("ANR");
        await Assert.That(tags["crash_source"]).IsEqualTo("ProcessExit:Anr");
        await Assert.That(tags["last_command"]).IsEqualTo("SaveProject");
    }

    [Test]
    public async Task Classify_from_raw_reclassifies_an_already_stored_payload()
    {
        var raw = EnvelopeParser.Parse(RecoveredAnrEnvelope).Events[0].EventEntry;

        var classification = EnvelopeParser.ClassifyFromRaw(raw);

        await Assert.That(classification).IsNotNull();
        await Assert.That(classification!.IsCrash).IsTrue();
        await Assert.That(classification.IsAnr).IsTrue();
    }

    [Test]
    public async Task Classify_from_raw_returns_null_for_unusable_payloads()
    {
        await Assert.That(EnvelopeParser.ClassifyFromRaw(null)).IsNull();
        await Assert.That(EnvelopeParser.ClassifyFromRaw("not json")).IsNull();
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
