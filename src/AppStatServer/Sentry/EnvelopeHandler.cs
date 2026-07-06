using System.IO.Compression;

namespace AppStatServer.Sentry;

public class EnvelopeHandler
{
    public EnvelopeHandler(WebApplication app)
    {
        app.MapPost("/api/1/envelope", async (HttpRequest request, IEventStorage eventStorage) =>
        {
            var body = await ReadBodyAsync(request);
            var parsed = EnvelopeParser.Parse(body);

            if (parsed.Events.Count > 0)
                await eventStorage.SaveEventsAsync(parsed.Events);

            if (parsed.Sessions.Count > 0)
                await eventStorage.SaveSessionsAsync(parsed.Sessions);

            return new EnvelopeResponse { Id = parsed.LastId };
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
