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
            if (session?.sid != null)
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

            result.Events.Add(MapEvent(eventEntry, entry, message));
            result.LastId = eventEntry.event_id ?? result.LastId;
        }

        // Envelope header ({"sdk...) and section markers ({"type...) carry no payload we persist.
    }

    private static AppEvent MapEvent(EventEntry eventEntry, string rawEntry, string message)
    {
        return new AppEvent
        {
            Id = eventEntry.event_id ?? Guid.NewGuid().ToString(),
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
            Release = session.attrs?.release ?? string.Empty,
            Environment = session.attrs?.environment ?? string.Empty,
        };
    }

    private static string ExtractVersion(string? release)
    {
        if (string.IsNullOrWhiteSpace(release))
            return string.Empty;

        var match = VersionRegex().Match(release);
        return match.Success ? match.Groups[1].Value : release;
    }

    private static string? BuildStackTrace(EventEntry eventEntry)
    {
        var sb = new StringBuilder();

        if (eventEntry.exception?.values is { Count: > 0 } exceptions)
            foreach (var ex in exceptions)
                sb.AppendLine($"{ex.type}: {ex.value}");

        var threads = eventEntry.threads?.values;
        if (threads != null)
            foreach (var thread in threads)
            {
                var frames = thread.stacktrace?.frames;
                if (frames == null || frames.Count == 0)
                    continue;

                if (thread.crashed)
                    sb.AppendLine($"Thread {thread.id} (crashed):");

                // Sentry sends frames oldest-first; reverse so the throwing frame is on top.
                for (var i = frames.Count - 1; i >= 0; i--)
                {
                    var f = frames[i];
                    var location = !string.IsNullOrEmpty(f.filename)
                        ? $" in {f.filename}{(f.lineno.HasValue ? $":line {f.lineno}" : string.Empty)}"
                        : !string.IsNullOrEmpty(f.package)
                            ? $" [{f.package}]"
                            : string.Empty;
                    sb.AppendLine($"   at {f.function}{location}");
                }
            }

        var stackTrace = sb.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace;
    }

    [GeneratedRegex(@"(\d+\.\d+\.\d+)(\+\w+)?$")]
    private static partial Regex VersionRegex();
}
