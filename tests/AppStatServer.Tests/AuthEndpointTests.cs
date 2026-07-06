using System.Net;
using System.Net.Http.Json;
using System.Text;
using AppStatServer;
using LiteDB;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppStatServer.Tests;

// Verifies the cookie-auth boundary: the read API is protected, the ingest endpoint
// stays open for SDKs, and valid credentials unlock the dashboard API.
public class AuthEndpointTests
{
    private static WebApplicationFactory<Program> CreateFactory()
    {
        var db = new LiteDatabase(new MemoryStream());
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Username", "tester");
            builder.UseSetting("Auth:Password", "test-pass");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventStorage>();
                services.AddSingleton<IEventStorage>(_ => new LiteDbEventStorage(db));
            });
        });
    }

    [Test]
    public async Task Read_api_requires_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/events");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Ingest_endpoint_is_anonymous()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        const string sessionEnvelope =
            """{"sid":"anon-session","init":true,"started":"2024-04-18T20:00:00Z","timestamp":"2024-04-18T20:05:00Z","seq":1,"duration":10,"errors":0,"attrs":{"release":"app@1.0.0","environment":"production"}}""";
        using var content = new StringContent(sessionEnvelope, Encoding.UTF8);

        var response = await client.PostAsync("/api/1/envelope", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsJsonAsync("/login", new { username = "tester", password = "nope" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Login_then_read_api_succeeds()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/login", new { username = "tester", password = "test-pass" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var events = await client.GetAsync("/api/events");
        await Assert.That(events.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var stats = await client.GetAsync("/api/stats");
        await Assert.That(stats.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Analytics_and_grouping_endpoints_respond_when_authed()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/login", new { username = "tester", password = "test-pass" });
        login.EnsureSuccessStatusCode();

        foreach (var url in new[] { "/api/analytics", "/api/analytics?days=7", "/api/event-groups", "/api/event-groups?release=1.0.0&os=iOS", "/api/crash-groups", "/api/facets" })
        {
            var res = await client.GetAsync(url);
            await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task Analytics_requires_authentication()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.GetAsync("/api/analytics");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
