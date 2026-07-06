# AppStatServer

A Sentry SDK–compatible .NET server that ingests application events and crashes.
Apps point their Sentry SDK at this server, and it parses the envelopes into events
and sessions and stores them.

Inspired by the [glitchtip.com](https://glitchtip.com) project.

> Current status: proof of concept.

## What it does

- Accepts Sentry envelopes on `POST /api/1/envelope` (gzip aware).
- Parses event and session entries, extracting message, level, release, stacktrace,
  trace/span ids, OS and user info.
- Persists everything to an embedded [LiteDB](https://www.litedb.org/) database — no
  external database required.
- Serves a built-in web dashboard behind a cookie login, with AppCenter-style pages:
  - **Overview** — stat cards, events-per-day chart, level breakdown, recent events & sessions.
  - **Analytics** — active & new users over 7/14/30/90 days, MAU/WAU/DAU, sessions per
    day, session-duration histogram, active users by app version, OS distribution and
    top devices.
  - **Events** — all non-crash events grouped by message (count / affected users / last
    seen), filterable by app version and OS.
  - **Crashes** — crashes grouped by signature, with stack traces, filterable by app
    version and OS.

### Endpoints

| Method | Route                | Auth   | Description                          |
|--------|----------------------|--------|--------------------------------------|
| POST   | `/api/1/envelope`    | none   | Ingest a Sentry envelope             |
| GET    | `/`                  | cookie | Dashboard SPA (redirects to login)   |
| POST   | `/login`             | none   | Sign in (`{ "username", "password" }`) → sets cookie |
| POST   | `/logout`            | none   | Clear the session cookie             |
| GET    | `/api/me`            | cookie | Current signed-in user               |
| GET    | `/api/events`        | cookie | Last 100 events                      |
| GET    | `/api/events/{id}`   | cookie | A single event by id                 |
| GET    | `/api/sessions`      | cookie | Last 100 sessions                    |
| GET    | `/api/stats`         | cookie | Aggregated counts for the Overview   |
| GET    | `/api/analytics?days=` | cookie | Usage analytics over a window (days 1–90, default 30) |
| GET    | `/api/event-groups?release=&os=` | cookie | Non-crash events grouped by message (optional version/OS filter) |
| GET    | `/api/crash-groups?release=&os=` | cookie | Crashes grouped by signature (optional version/OS filter) |
| GET    | `/api/facets`        | cookie | Distinct releases & OSes for the filter dropdowns |

The dashboard is a single protected page with client-side routing
(`#/overview`, `#/analytics`, `#/events`, `#/crashes`); its data comes from the
`/api/*` endpoints above.

The ingest endpoint stays open — SDKs authenticate with their DSN, not the dashboard
cookie. Everything under `/api/*` and the dashboard page require a login.

### Authentication

A single set of credentials comes from configuration and can be overridden with
environment variables (double-underscore syntax):

```bash
Auth__Username=admin
Auth__Password=your-strong-password
```

The default in `appsettings.json` is `admin` / `changeme` so the app runs out of the
box; the server logs a warning while the default password is in use. **Override
`Auth__Password` before any real deployment.**

## Tech stack

- .NET 10, ASP.NET Core Minimal API (`WebApplication.CreateSlimBuilder`)
- LiteDB for storage
- TUnit for tests

## Repository layout

```
AppStatServer.slnx              # solution (slnx format)
src/AppStatServer/          # the server
tests/AppStatServer.Tests/  # TUnit tests
samples/ConsoleTestClient/      # console app that emits Sentry events for manual testing
```

## Getting started

```bash
# build everything
dotnet build AppStatServer.slnx

# run the server (listens on http://localhost:5012)
dotnet run --project src/AppStatServer

# run the tests (Microsoft.Testing.Platform mode, enabled via global.json)
dotnet test --solution AppStatServer.slnx
```

To generate some traffic, run the sample client against a running server:

```bash
dotnet run --project samples/ConsoleTestClient
```

Then open the dashboard at `http://localhost:5012/` and sign in (default `admin` /
`changeme`) to inspect the collected events and sessions.
