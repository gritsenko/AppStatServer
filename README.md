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

### Endpoints

| Method | Route               | Description                       |
|--------|---------------------|-----------------------------------|
| POST   | `/api/1/envelope`   | Ingest a Sentry envelope          |
| GET    | `/events`           | Last 100 events                   |
| GET    | `/events/{id}`      | A single event by id              |
| GET    | `/sessions`         | Last 100 sessions                 |

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

Then inspect the collected data at `http://localhost:5012/events` and
`http://localhost:5012/sessions`.
