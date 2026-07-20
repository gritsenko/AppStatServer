using System.IO.Compression;
using AppStatServer.Data;

namespace AppStatServer.Tracking;

// Custom-event ingest, mirroring EnvelopeHandler. Anonymous like the Sentry envelope
// route: clients post directly, they don't carry the dashboard auth cookie.
public class TrackEventHandler
{
    public TrackEventHandler(WebApplication app, string corsPolicy)
    {
        app.MapPost("/api/track", async (HttpRequest request, IEventStorage eventStorage) =>
        {
            var body = await ReadBodyAsync(request);
            var events = TrackEventParser.Parse(body);

            // Split off reserved infrastructure events (name starting with '@'): "@session" active-time
            // pings become ClientSession upserts and are NOT stored among product events; any other
            // reserved event is dropped so it can never pollute product reports.
            var productEvents = new List<TrackEvent>(events.Count);
            var sessions = new List<Data.ClientSession>();
            foreach (var e in events)
            {
                if (!ClientSessionMapper.IsReserved(e.Name))
                {
                    productEvents.Add(e);
                    continue;
                }

                if (e.Name == ClientSessionMapper.SessionEventName &&
                    ClientSessionMapper.FromTrackEvent(e) is { } session)
                    sessions.Add(session);
            }

            if (productEvents.Count > 0)
                await eventStorage.SaveTrackEventsAsync(productEvents);
            if (sessions.Count > 0)
                await eventStorage.SaveClientSessionsAsync(sessions);

            return Results.Ok(new { accepted = productEvents.Count + sessions.Count });
        }).RequireCors(corsPolicy);
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        Stream stream = request.Body;

        if (request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
            stream = new GZipStream(stream, CompressionMode.Decompress);

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
