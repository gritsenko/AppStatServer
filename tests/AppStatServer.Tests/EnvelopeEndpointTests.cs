using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AppStatServer;
using AppStatServer.Data;
using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppStatServer.Tests;

// End-to-end tests that drive the real ASP.NET Core pipeline (routing, gzip
// decompression, EnvelopeHandler, storage) via WebApplicationFactory<Program>.
// The on-disk LiteDB storage is swapped for an in-memory instance so tests touch no files.
public class EnvelopeEndpointTests
{
    private const string EventEnvelope =
        """{"sdk":{"name":"sentry.dotnet","version":"4.13.0"},"event_id":"e2e-1","sent_at":"2024-04-18T20:00:01Z"}""" + "\n" +
        """{"type":"event","length":42}""" + "\n" +
        """{"event_id":"e2e-1","timestamp":"2024-04-18T20:00:00Z","level":"error","release":"myapp@1.2.3","exception":{"values":[{"type":"System.Exception","value":"boom"}]},"threads":{"values":[{"id":1,"crashed":true,"stacktrace":{"frames":[{"function":"Inner","filename":"Inner.cs","lineno":10,"in_app":true}]}}]}}""";

    private const string SessionEnvelope =
        """{"sid":"e2e-session","did":"device-1","init":true,"started":"2024-04-18T20:00:00Z","timestamp":"2024-04-18T20:05:00Z","seq":2,"duration":300,"errors":1,"attrs":{"release":"myapp@1.2.3","environment":"production"}}""";

    private static WebApplicationFactory<Program> CreateFactory()
    {
        // One in-memory db per factory => per-test isolation.
        var db = new LiteDatabase(new MemoryStream());
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Deterministic dashboard credentials for the test host.
            builder.UseSetting("Auth:Username", "tester");
            builder.UseSetting("Auth:Password", "test-pass");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventStorage>();
                services.AddSingleton<IEventStorage>(_ => new LiteDbEventStorage(db));
            });
        });
    }

    // The read API is behind cookie auth; sign in so subsequent GETs are authorized.
    // CreateClient() keeps a cookie container, so the auth cookie sticks to the client.
    private static async Task<HttpClient> CreateAuthedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/login", new { username = "tester", password = "test-pass" });
        login.EnsureSuccessStatusCode();
        return client;
    }

    [Test]
    public async Task Posting_gzipped_event_envelope_persists_and_exposes_event()
    {
        await using var factory = CreateFactory();
        using var client = await CreateAuthedClientAsync(factory);

        var response = await client.PostAsync("/api/1/envelope", GzipEnvelope(EventEnvelope));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await client.GetFromJsonAsync<List<AppEvent>>("/api/events");

        await Assert.That(events).IsNotNull();
        await Assert.That(events!.Count).IsEqualTo(1);

        var ev = events[0];
        await Assert.That(ev.Id).IsEqualTo("e2e-1");
        await Assert.That(ev.Message).IsEqualTo("boom");
        await Assert.That(ev.Release).IsEqualTo("1.2.3");
        await Assert.That(ev.IsCrash).IsTrue();
        await Assert.That(ev.StackTrace!).Contains("Inner.cs:line 10");
    }

    [Test]
    public async Task Posting_uncompressed_session_envelope_persists_session()
    {
        await using var factory = CreateFactory();
        using var client = await CreateAuthedClientAsync(factory);

        // No Content-Encoding header => the handler must read the body as-is.
        using var content = new StringContent(SessionEnvelope, Encoding.UTF8);
        var response = await client.PostAsync("/api/1/envelope", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var sessions = await client.GetFromJsonAsync<List<AppSession>>("/api/sessions");

        await Assert.That(sessions).IsNotNull();
        await Assert.That(sessions!.Count).IsEqualTo(1);
        await Assert.That(sessions[0].Id).IsEqualTo("e2e-session");
        await Assert.That(sessions[0].Environment).IsEqualTo("production");
    }

    [Test]
    public async Task Posting_event_with_blank_event_id_persists_without_500()
    {
        // Regression: a blank event_id used to reach LiteDB as an empty string _id, which it
        // tried to auto-generate an ObjectId for and then threw casting back to string (HTTP 500).
        const string blankIdEnvelope =
            """{"sdk":{"name":"sentry.dotnet","version":"4.13.0"},"event_id":"","sent_at":"2024-04-18T20:00:01Z"}""" + "\n" +
            """{"type":"event"}""" + "\n" +
            """{"event_id":"","timestamp":"2024-04-18T20:00:00Z","level":"info","logentry":{"message":"blank id"}}""";

        await using var factory = CreateFactory();
        using var client = await CreateAuthedClientAsync(factory);

        using var content = new StringContent(blankIdEnvelope, Encoding.UTF8);
        var response = await client.PostAsync("/api/1/envelope", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await client.GetFromJsonAsync<List<AppEvent>>("/api/events");
        await Assert.That(events!.Count).IsEqualTo(1);
        await Assert.That(events[0].Id).IsNotEmpty();
        await Assert.That(events[0].Message).IsEqualTo("blank id");
    }

    private static ByteArrayContent GzipEnvelope(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-sentry-envelope");
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }
}
