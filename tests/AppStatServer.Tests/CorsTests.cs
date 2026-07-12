using System.Net;
using LiteDB;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppStatServer.Tests;

// Verifies CORS on the anonymous ingest endpoints. A browser SDK on another origin (e.g. the
// web build at https://app.pix2d.com) issues a preflight OPTIONS before POSTing; that preflight
// must succeed with an Access-Control-Allow-Origin header, otherwise the browser reports a
// "CORS error" and never sends the event. Before the CORS policy was added, the preflight
// matched no route and returned 405.
public class CorsTests
{
    private static WebApplicationFactory<Program> CreateFactory(string? allowedOrigins = null)
    {
        var db = new LiteDatabase(new MemoryStream());
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (allowedOrigins is not null)
                builder.UseSetting("Cors:AllowedOrigins", allowedOrigins);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventStorage>();
                services.AddSingleton<IEventStorage>(_ => new LiteDbEventStorage(db));
            });
        });
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return request;
    }

    [Test]
    [Arguments("/api/track")]
    [Arguments("/api/1/envelope")]
    public async Task Preflight_from_any_origin_is_allowed_by_default(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = Preflight(path, "https://app.pix2d.com");
        var response = await client.SendAsync(request);

        // The preflight must not 405 (the pre-fix behaviour) and must carry an allow-origin header.
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
    }

    [Test]
    public async Task Configured_allowlist_permits_listed_origin()
    {
        await using var factory = CreateFactory("https://app.pix2d.com, https://pix2d.com");
        using var client = factory.CreateClient();

        using var request = Preflight("/api/track", "https://app.pix2d.com");
        var response = await client.SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin"))
            .Contains("https://app.pix2d.com");
    }

    [Test]
    public async Task Configured_allowlist_rejects_unlisted_origin()
    {
        await using var factory = CreateFactory("https://app.pix2d.com");
        using var client = factory.CreateClient();

        using var request = Preflight("/api/track", "https://evil.example");
        var response = await client.SendAsync(request);

        // No allow-origin header for an unlisted origin, so the browser blocks the request.
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }
}
