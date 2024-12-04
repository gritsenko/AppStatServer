using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AppStatServerLite.Data;

namespace AppStatServerLite.Sentry;

public class EnvelopeHandler
{
    private ConcurrentQueue<AppEvent> _events = new ();

    public EnvelopeHandler(WebApplication app)
    {
        app.MapPost("/api/1/envelope", async (HttpRequest request, IEventStorage eventStorage) =>
        {
            var body = request.Body;
            var requestBody = await DecompressStream(body);
            var entries = requestBody.Split('\n');
            var lastId = "0";

            if (entries.Length > 0)
                foreach (var entry in entries)
                    lastId = ProcessEntry(entry, _events);

            var events = new List<AppEvent>();
            while (_events.Count > 0)
                if (_events.TryDequeue(out var ev)) events.Add(ev);

            await eventStorage.SaveEventsAsync(events);
            //Console.WriteLine("count: " + _events.Count);

            return new EnvelopeResponse { Id = lastId };
        });

    }

    static async Task<string> DecompressStream(Stream compressedStream)
    {
        var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using StreamReader streamReader = new StreamReader(gzipStream);
        return await streamReader.ReadToEndAsync();
    }

    string ProcessEntry(string entry, ConcurrentQueue<AppEvent> eventsQueue)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return "0";

        if (entry.StartsWith("{\"sdk"))
        {
            var sdkEntry = JsonSerializer.Deserialize<SdkEntry>(entry);
        }

        if (entry.StartsWith("{\"type"))
        {
            var sectionEntry = JsonSerializer.Deserialize<SectionEntry>(entry);
        }

        if (entry.StartsWith("{\"sid"))
        {
            var sessionEntry = JsonSerializer.Deserialize<SessionEntry>(entry);
        }

        if (entry.StartsWith("{\"event_id") || entry.StartsWith("{\"modules"))
        {
            var eventEntry = JsonSerializer.Deserialize<EventEntry>(entry);

            var message = eventEntry?.exception?.values?.FirstOrDefault()?.value
                          ?? eventEntry?.logentry?.message;

            string pattern = @"(\d+\.\d+\.\d+)(\+\w+)?$";

            var input = eventEntry.release;
            // Match the pattern in the input string
            Match match = Regex.Match(input, pattern);
            var versionNumber = input;

            if (match.Success)
            {
                // Extract the version number from the matched group
                versionNumber = match.Groups[1].Value;
                Console.WriteLine("Version Number: " + versionNumber);
            }
            else
            {
                Console.WriteLine("No version number found in the input string.");
            }

            var isError = eventEntry?.exception != null;
            var isCrash = eventEntry?.threads?.values?.Any(x => x.crashed) ?? false;
            var level = eventEntry?.level ?? "-";
            var sessionId = "";
            var spanId = eventEntry.contexts?.trace?.span_id;
            var traceId = eventEntry.contexts?.trace?.trace_id;
            var os = eventEntry.contexts?.os?.raw_description;
            var user = eventEntry.user?.id ?? Guid.Empty.ToString();

            if (!string.IsNullOrWhiteSpace(message))
            {
                eventsQueue.Enqueue(new AppEvent()
                {
                    Id = eventEntry.event_id,
                    Timestamp = eventEntry.timestamp,
                    SessionId = sessionId,
                    Message = message,
                    EventEntry = "", //entry,
                    IsCrash = isCrash,
                    IsError = isError,
                    Level = level,
                    Release = versionNumber,
                    SpanId = spanId,
                    TraceId = traceId,
                    Os = os,
                    UserId = user
                });

                return eventEntry.event_id;
            }
        }

        return "0";
    }
}