---
name: run-and-verify
description: Use when building, testing, running, or verifying the StudentManagement solution — e.g. "build", "run tests", "run the app", "verify it works", "check CI", "docker up", or after making code changes. Covers local run, Docker, SQL Server, test commands, and expected infra (Ollama, ChromaDB).
---

# Build, Test, Run & Verify (StudentManagement)

## Build

```powershell
dotnet build
```
Solution: `StudentManagement.sln` (5 projects). Target .NET 8.

## Test

```powershell
dotnet test
```
- xUnit + Moq. Repository tests use EF Core SQLite in-memory; service tests use Moq.
- **Known flaky test:** `CourseRepositoryTests.UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition` (SQLite concurrency). If it fails, retry once; on second failure, flag it and continue — do NOT modify it.
- Tests do NOT require Ollama/ChromaDB/SQL Server (mocked or in-memory).

## Run locally

```powershell
dotnet run --project StudentManagement
```
App listens on the ports in `Properties/launchSettings.json` (Kestrel).

**Prerequisites for full functionality:**
- **SQL Server** with the `ConnectionStrings:DatabaseConnection` (see `appsettings.json`). Startup runs `MigrateAsync()` + `SeedAdmin` with 10 retries / 5s — DB must be reachable or the app exits.
- **Ollama** running at `Chat:Endpoint` (`http://localhost:11434`) with the model in `Chat:Model` (default `qwen2.5:3b-instruct`). Not required to boot the app, only for chat.
- **ChromaDB** at `Chroma:Endpoint` (`http://localhost:8000`) for RAG. Not required to boot, only for RAG retrieval.
- **MCP Jira server** (`Chat:Mcp`) — enabled only in Development (`appsettings.Development.json`). Disabled in Production.

## Docker

```powershell
docker-compose up
```
- Web on **:8080**, SQL Server 2022 (`db` service) with healthcheck. Volumes: `sql_data`, `dp_keys`.
- First boot takes ~30-60s for SQL Server cold start; the migration retry loop handles it.
- Production overrides (`appsettings.Production.json`) point chat at `http://host.docker.internal:1234` and disable MCP.

## Verification checklist after changes

1. `dotnet build` exits 0
2. `dotnet test` green (excluding the known flaky test after one retry)
3. If schema changed: migration exists and app boots (auto-applies)
4. If AI/chat changed: Ollama + ChromaDB running, `/Chat` responds
5. If seed data changed: `DbInitializer.SeedAdmin` still idempotent (2 admins, 4 roles)
