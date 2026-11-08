# AGENTS.md — StudentManagement

## Repo structure (clean architecture)

| Folder | Role |
|---|---|
| `StudentManagement/` | ASP.NET Core 8 web app (entrypoint, controllers, config) |
| `StudentManagement.Application/` | Business logic, services, interfaces |
| `StudentManagement.Domain/` | Domain models, value objects (no deps) |
| `StudentManagement.Infrastructure/` | EF Core DbContext, repos, migrations |
| `StudentManagement.Tests/` | xUnit + Moq tests |

Solution: `StudentManagement.sln`

## Commands

```
dotnet build StudentManagement.sln
dotnet test StudentManagement.sln
```

CI runs on `develop` branch (`dotnet restore → build → test`).

## Run / local dev

- `appsettings.json` connection string defaults to `Server=localhost;…;Trusted_Connection=True` (Windows auth).
- Docker compose: `docker-compose up` — spins up web + SQL Server (sa / `YourStrong@Password1`).
- DB migrations run automatically at startup (with retry).

## Gotchas

- `wwwroot/lib/` is gitignored — Bower/npm assets are restored on build.
- `Program.cs` has startup migration+seeding block with retry loop — do not remove if changing DB setup.
- Chat service (`ChatService`) targets Ollama at `localhost:11434` by default.
- Google OAuth credentials are placeholders in `appsettings.json` — set via user secrets or env vars.
- Rate limiting configured via `AspNetCoreRateLimit` — endpoints in `IpRateLimiting` section.
- EF Core migrations live in `StudentManagement.Infrastructure/Migrations/`.
- Tests use `Microsoft.EntityFrameworkCore.Sqlite` — no real SQL Server needed for unit tests.
