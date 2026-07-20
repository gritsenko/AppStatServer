using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AppStatServer.Data;

namespace AppStatServer.Sentry;

public class ParsedEnvelope
{
    public List<AppEvent> Events { get; } = new();
    public List<AppSession> Sessions { get; } = new();
    public string LastId { get; set; } = "0";
}

/// <summary>
/// Pure (no I/O) parser for a decompressed Sentry envelope body.
/// Each envelope is a sequence of newline-separated JSON entries; this turns the
/// event and session entries into <see cref="AppEvent"/> / <see cref="AppSession"/> records.
/// Kept free of HTTP/storage concerns so it can be unit tested directly.
/// </summary>
public static partial class EnvelopeParser
{
    public static ParsedEnvelope Parse(string body)
    {
        var result = new ParsedEnvelope();

        if (string.IsNullOrWhiteSpace(body))
            return result;

        foreach (var entry in body.Split('\n'))
            ProcessEntry(entry, result);

        // Best-effort: link events to a session present in the same envelope.
        if (result.Sessions.Count > 0)
        {
            var sid = result.Sessions[^1].Id;
            foreach (var ev in result.Events)
                if (string.IsNullOrEmpty(ev.SessionId))
                    ev.SessionId = sid;
        }

        return result;
    }

    private static void ProcessEntry(string entry, ParsedEnvelope result)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return;

        if (entry.StartsWith("{\"sid"))
        {
            var session = JsonSerializer.Deserialize<SessionEntry>(entry);
            // The sid is the session's primary key (sessions are upserted by it). A missing
            // or blank sid can't be a key — and would blow up on persist — so drop it.
            if (session is not null && !string.IsNullOrWhiteSpace(session.sid))
                result.Sessions.Add(MapSession(session));
            return;
        }

        if (entry.StartsWith("{\"event_id") || entry.StartsWith("{\"modules"))
        {
            var eventEntry = JsonSerializer.Deserialize<EventEntry>(entry);
            if (eventEntry == null)
                return;

            var message = eventEntry.exception?.values?.FirstOrDefault()?.value
                          ?? eventEntry.logentry?.message;

            if (string.IsNullOrWhiteSpace(message))
                return;

            var mapped = MapEvent(eventEntry, entry, message);
            result.Events.Add(mapped);
            // Echo back the event id; if the payload omitted one we fall back to the
            // generated id we just stored, never an empty string.
            result.LastId = mapped.Id;
        }

        // Envelope header ({"sdk...) and section markers ({"type...) carry no payload we persist.
    }

    private static AppEvent MapEvent(EventEntry eventEntry, string rawEntry, string message)
    {
        return new AppEvent
        {
            // A blank id (not just null) must not reach storage: LiteDB tries to auto-generate
            // an ObjectId for an empty string _id and then fails casting it back to string.
            Id = string.IsNullOrWhiteSpace(eventEntry.event_id) ? Guid.NewGuid().ToString() : eventEntry.event_id,
            Timestamp = eventEntry.timestamp,
            SessionId = string.Empty,
            Message = message,
            EventEntry = rawEntry,
            StackTrace = BuildStackTrace(eventEntry),
            IsCrash = eventEntry.threads?.values?.Any(x => x.crashed) ?? false,
            IsError = eventEntry.exception != null,
            Level = eventEntry.level ?? "-",
            Release = ExtractVersion(eventEntry.release),
            SpanId = eventEntry.contexts?.trace?.span_id,
            TraceId = eventEntry.contexts?.trace?.trace_id,
            Os = eventEntry.contexts?.os?.raw_description,
            DeviceModel = eventEntry.contexts?.device?.model
                          ?? eventEntry.contexts?.device?.family,
            UserId = eventEntry.user?.id ?? Guid.Empty.ToString(),
        };
    }

    private static AppSession MapSession(SessionEntry session)
    {
        return new AppSession
        {
            Id = session.sid ?? string.Empty,
            DeviceId = session.did,
            Started = session.started,
            Timestamp = session.timestamp,
            Seq = session.seq,
            Duration = session.duration,
            Errors = session.errors,
            Init = session.init,
            Release = ExtractVersion(session.attrs?.release),
            Environment = session.attrs?.environment ?? string.Empty,
        };
    }

    // Shared with TrackEventParser so every ingest path stores the same "1.2.3" form and
    // release breakdowns don't split into "myapp@1.2.3" vs "1.2.3" buckets.
    public static string ExtractVersion(string? release)
    {
        if (string.IsNullOrWhiteSpace(release))
            return string.Empty;

        var match = VersionRegex().Match(release);
        return match.Success ? match.Groups[1].Value : release;
    }

    private static string? BuildStackTrace(EventEntry eventEntry)
    {
        var sb = new StringBuilder();
        var usefulFrames = false;

        if (eventEntry.exception?.values is { Count: > 0 } exceptions)
            foreach (var ex in exceptions)
            {
                sb.AppendLine($"{ex.type}: {ex.value}");
                // The SDK attaches the managed stack to the exception entry itself — reading
                // only thread frames (below) drops it, which is why AOT/handled errors looked
                // frame-less. Render these too.
                if (AppendFrames(sb, ex.stacktrace?.frames))
                    usefulFrames = true;
            }

        var threads = eventEntry.threads?.values;
        if (threads != null)
            foreach (var thread in threads)
            {
                var frames = thread.stacktrace?.frames;
                if (frames == null || frames.Count == 0)
                    continue;

                if (thread.crashed)
                    sb.AppendLine($"Thread {thread.id} (crashed):");

                if (AppendFrames(sb, frames))
                    usefulFrames = true;
            }

        // Frame-less event (trimmed/AOT builds strip managed frames; native-only frames carry no
        // symbol). Fall back to the capture-site text stack the app attaches as an extra, so the
        // reported stack is never just "Type: message".
        if (!usefulFrames && TryGetExtraString(eventEntry, "stack_trace_text", out var textStack))
        {
            sb.AppendLine();
            sb.AppendLine("=== stack_trace_text (capture-site fallback) ===");
            sb.AppendLine(textStack);
        }

        var stackTrace = sb.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace;
    }

    // Renders frames oldest-last (Sentry sends them oldest-first, so we reverse to put the
    // throwing frame on top). Returns true only if at least one frame carried a symbol
    // (function or filename) — native-only frames don't count, so the stack_trace_text
    // fallback still kicks in for symbol-less AOT stacks.
    private static bool AppendFrames(StringBuilder sb, List<StacktraceFrame>? frames)
    {
        if (frames == null || frames.Count == 0)
            return false;

        var useful = false;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var f = frames[i];
            var location = !string.IsNullOrEmpty(f.filename)
                ? $" in {f.filename}{(f.lineno.HasValue ? $":line {f.lineno}" : string.Empty)}"
                : !string.IsNullOrEmpty(f.package)
                    ? $" [{f.package}]"
                    : string.Empty;
            sb.AppendLine($"   at {f.function}{location}");

            if (!string.IsNullOrEmpty(f.function) || !string.IsNullOrEmpty(f.filename))
                useful = true;
        }

        return useful;
    }

    private static bool TryGetExtraString(EventEntry eventEntry, string key, out string value)
    {
        value = string.Empty;
        if (eventEntry.extra is null || !eventEntry.extra.TryGetValue(key, out var el))
            return false;

        var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        if (string.IsNullOrWhiteSpace(s))
            return false;

        value = s;
        return true;
    }

    /// <summary>
    /// Pulls the app-attached <c>extra</c> context out of a raw event entry (the JSON persisted in
    /// <see cref="AppEvent.EventEntry"/>). Used by the diagnostics MCP tool to surface fields like
    /// <c>exception_chain</c>, <c>app_context</c> and <c>last_command</c> that the stack alone omits.
    /// Best-effort: malformed or absent extras yield an empty dictionary rather than throwing.
    /// </summary>
    public static Dictionary<string, string> ExtractExtras(string? rawEntry)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(rawEntry))
            return result;

        try
        {
            var eventEntry = JsonSerializer.Deserialize<EventEntry>(rawEntry);
            if (eventEntry?.extra is null)
                return result;

            foreach (var (k, el) in eventEntry.extra)
            {
                var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    result[k] = s;
            }
        }
        catch (JsonException)
        {
            // A non-event entry or truncated payload — just return what we have.
        }

        return result;
    }

    [GeneratedRegex(@"(\d+\.\d+\.\d+)(\+\w+)?$")]
    private static partial Regex VersionRegex();
}
