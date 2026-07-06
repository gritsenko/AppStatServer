using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AppStatServer;
using AppStatServer.Auth;
using AppStatServer.Sentry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateSlimBuilder(args);

var dbFileName = builder.Configuration["LiteDbFilePath"];
builder.Services.AddSingleton<IEventStorage, LiteDbEventStorage>(_ => new LiteDbEventStorage(dbFileName));

// Cookie authentication protects the dashboard and the read API. The single set of
// credentials comes from configuration (Auth:Username / Auth:Password), overridable
// via the Auth__Username / Auth__Password environment variables.
// Persist the data-protection keys (used to sign auth cookies) to a stable directory when
// configured, so cookies survive restarts. Without DataProtection:KeyPath the framework
// default location is used (fine for local dev).
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
var dataProtection = builder.Services.AddDataProtection();
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    Directory.CreateDirectory(dataProtectionKeyPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AppStatServer.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/login.html";

        // Browser navigations to a protected page get redirected to the login page;
        // API calls get a clean 401 instead (a redirect to HTML would confuse fetch()).
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var authUsername = app.Configuration["Auth:Username"] ?? "admin";
var authPassword = app.Configuration["Auth:Password"] ?? string.Empty;

// The DSN shown on the dashboard so an SDK can be pointed at this server. The ingest
// route is fixed at project id 1 (/api/1/envelope) and the public key is not validated,
// so the key is a display-only identifier (Sentry:PublicKey). By default the DSN host is
// taken from the incoming request; set Sentry:PublicUrl to pin a public base URL when the
// server sits behind a reverse proxy.
var sentryPublicKey = app.Configuration["Sentry:PublicKey"] ?? "appstatserver";
var sentryPublicUrl = app.Configuration["Sentry:PublicUrl"];

if (string.IsNullOrEmpty(authPassword))
    app.Logger.LogWarning(
        "Auth:Password is not set — nobody can log in. Set it via configuration or the Auth__Password environment variable.");
else if (authPassword == "changeme")
    app.Logger.LogWarning(
        "Auth:Password is still the default 'changeme'. Override it via the Auth__Password environment variable before deploying.");

// --- Authentication endpoints (anonymous) ---
app.MapPost("/login", async (LoginRequest login, HttpContext http) =>
{
    if (string.IsNullOrEmpty(authPassword) || !CredentialsMatch(login, authUsername, authPassword))
        return Results.Unauthorized();

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, authUsername)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Ok(new { username = authUsername });
});

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

// --- Dashboard page (protected). Lives outside wwwroot so it is only reachable authenticated. ---
var dashboardPath = Path.Combine(app.Environment.ContentRootPath, "Pages", "dashboard.html");
app.MapGet("/", () => Results.File(dashboardPath, "text/html; charset=utf-8"))
    .RequireAuthorization();

// --- Read API (protected) ---
var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/me", (HttpContext http) => Results.Ok(new { username = http.User.Identity?.Name }));
api.MapGet("/dsn", (HttpContext http) =>
{
    string scheme, authority;
    if (string.IsNullOrWhiteSpace(sentryPublicUrl))
    {
        scheme = http.Request.Scheme;
        authority = http.Request.Host.Value ?? string.Empty;
    }
    else
    {
        var uri = new Uri(sentryPublicUrl);
        scheme = uri.Scheme;
        authority = uri.Authority;
    }

    return Results.Ok(new { dsn = $"{scheme}://{sentryPublicKey}@{authority}/1" });
});
api.MapGet("/events", (IEventStorage es) => es.GetRecentEventsAsync());
api.MapGet("/events/{id}", async (string id, IEventStorage es) =>
    (await es.GetRecentEventsAsync()).FirstOrDefault(a => a.Id == id) is { } ev
        ? Results.Ok(ev)
        : Results.NotFound());
api.MapGet("/sessions", (IEventStorage es) => es.GetRecentSessionsAsync());
api.MapGet("/stats", (IEventStorage es) => es.GetStatsAsync());
api.MapGet("/analytics", (IEventStorage es, int? days) =>
    es.GetAnalyticsAsync(days is >= 1 and <= 90 ? days.Value : 30));
api.MapGet("/event-groups", (IEventStorage es, string? release, string? os) => es.GetEventGroupsAsync(false, release, os));
api.MapGet("/crash-groups", (IEventStorage es, string? release, string? os) => es.GetEventGroupsAsync(true, release, os));
api.MapGet("/facets", (IEventStorage es) => es.GetFacetsAsync());

// --- Ingest (anonymous: SDKs authenticate with their DSN, not the dashboard cookie) ---
_ = new EnvelopeHandler(app);

app.Run();

// Fixed-time comparison so a wrong password can't be guessed by timing the response.
static bool CredentialsMatch(LoginRequest login, string username, string password)
{
    var userOk = CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(login.Username ?? string.Empty),
        Encoding.UTF8.GetBytes(username));
    var passOk = CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(login.Password ?? string.Empty),
        Encoding.UTF8.GetBytes(password));

    return userOk && passOk;
}
