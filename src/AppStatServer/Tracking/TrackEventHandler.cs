using System.IO.Compression;

namespace AppStatServer.Tracking;

// Custom-event ingest, mirroring EnvelopeHandler. Anonymous like the Sentry envelope
// route: clients post directly, they don't carry the dashboard auth cookie.
public class TrackEventHandler
{
    public TrackEventHandler(WebApplication app)
    {
        app.MapPost("/api/track", async (HttpRequest request, IEventStorage eventStorage) =>
        {
            var body = await ReadBodyAsync(request);
            var events = TrackEventParser.Parse(body);

            if (events.Count > 0)
                await eventStorage.SaveTrackEventsAsync(events);

            return Results.Ok(new { accepted = events.Count });
        });
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
