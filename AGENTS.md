# StudentManagement

ASP.NET Core MVC system for students, courses, and enrollments (.NET 8, Clean Architecture, SQL Server)
with Identity auth, an AI chat/RAG assistant, a RAG evaluation harness, and Playwright E2E tests.

## Stack

- **.NET 8** MVC + attribute-routed REST API (`/api/students`, `/api/evaluation`), nullable + implicit usings
- **EF Core 8 / SQL Server** — writable `ApplicationDbContext` + read-only `ReadOnlyDbContext`; migrations auto-apply at startup
- **Identity** (cookie + Google, TOTP MFA, roles Admin/Teacher/Student/User) · **IP rate limiting** · **Redis-or-memory cache**
- **AI**: Microsoft.Extensions.AI → OpenAI-compatible endpoint (Ollama default), ChromaDB RAG, function tools, stdio MCP
- **Ops**: Serilog (JSON + rolling files), OpenTelemetry, Prometheus · **Tests**: xUnit + Moq, Playwright in `e2e/`

## Commands

| Task | Command |
|---|---|
| Build / unit tests | `dotnet build` · `dotnet test` |
| Run | `dotnet run --project StudentManagement` (http://localhost:5255) |
| E2E | `scripts/e2e.ps1` (`-Headed`, `-KeepDb`) · or `cd e2e; npx playwright test` |
| Docker stack | `docker-compose up` (web :8080 + SQL Server + Redis) — needs `AdditionalConfig__Path` set, see Landmines |
| New migration | `dotnet ef migrations add <Name>` in Infrastructure — never run `database update` by hand |
| RAG evaluation | `dotnet run --project StudentManagement -- evaluate --help` |

Seeded admin: `admin@admin.com` / `Admin123` (and `bhugolbijoy003@gmail.com`).

## Non-negotiable rules

1. **Layering:** Domain (no deps) ← Application; Domain ← Infrastructure; Web → Application + Infrastructure + Domain; Tests → Application + Web.
   Never let Domain depend on anything, or Application depend on Infrastructure/Web.
2. **The REST API never binds or serializes domain entities** — use `Application/DTOs/`; don't add new endpoints that bind entities directly either.
3. **Writes go Application service → repository.** Repositories use `ApplicationDbContext` for writes, `ReadOnlyDbContext` + `AsNoTracking()` for `*ReadOnly` reads; `Include()` belongs in the repository, not the service.
4. **Style:** file-scoped namespaces, constructor injection with `_camelCase` fields, `Async` suffix on async methods, no XML docs (except `[Description]` on `ChatTools` methods), test names `{Method}_{Scenario}_Returns{Expected}`.
5. **Schema change ⇒ EF Core migration** (startup applies it with 10 retries × 5 s). No raw SQL, no hand-edited migration history.
6. **Never modify `CourseRepositoryTests.UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition`** — known-flaky SQLite test: retry once, then flag.
7. **No secrets in `appsettings*.json`.** Keep placeholders/empty strings in committed config; real values live in user-secrets locally (Web project has a `UserSecretsId`) or env vars (`Section__Key`) in Docker/CI. Older commits on other branches still contain leaked keys, so rotate anything reused.

## Where the detail lives

This file is the index, not the manual. Read the matching sub-agent file before working in an area — each is also directly delegable.

| You need… | Read / delegate to |
|---|---|
| **Student** work (MVC + REST, DTOs, cache) | `.opencode/agent/student-expert.md` (runbook) → `web-api-expert.md`, `domain-expert.md` |
| **Course** work (counts cache, client ResponseCache) | Feature slice map → `domain-expert.md` (caching), `web-api-expert.md` (quirks) |
| **Enrollment** work (cascade, stale counts, unawaited delete) | Feature slice map → `domain-expert.md` (invariants, caching) |
| **Auth / admin** (login, MFA, Google, reset, roles) | `.opencode/agent/identity-security-expert.md` |
| **Chat / RAG** (prompt, tools, ChromaDB, MCP) | `.opencode/agent/chat-rag-expert.md` |
| **Evaluation** (dataset, metrics, CLI, CI gate) | `.opencode/agent/rag-evaluation-expert.md` |
| Cross-cutting: entities, invariants, archiving, risks | `.opencode/agent/domain-expert.md` |
| Cross-cutting: file map, DI lifetimes, config, migrations, logging | `.opencode/agent/architecture-expert.md` |
| Cross-cutting: routes, status codes, DTO/PATCH, Razor/jQuery | `.opencode/agent/web-api-expert.md` |
| TDD tests / feature code / build fixes / diff review | `.opencode/agent/{test-planner,implementer,self-healer,pr-reviewer}.md` |
| Task recipes | `.opencode/skills/*/SKILL.md` — add-migration, ai-chat-tools, rag-knowledge, playwright-e2e, run-and-verify, tdd-feature |

The `*-expert` agents explain, map, and audit; they do not edit (except `chat-rag-expert` and `rag-evaluation-expert` in their own area). **Runbooks** (`student-expert`, `chat-rag-expert`, `rag-evaluation-expert`) describe *procedure* — files, flows, checklists; the reference docs (`domain`, `architecture`, `web-api`, `identity-security`) are authoritative on *rules*, and win on conflict. Hand code changes to `implementer`.

## Default workflow

`test-planner` (RED) → `implementer` (GREEN) → `self-healer` only if build/test fails → `pr-reviewer` before commit.

## Landmines

- **`AdditionalConfig:Path` is required** (`optional: false`) — `Program.cs` loads `C:\inetpub\appsettings.json` and throws on startup when missing; override with `AdditionalConfig__Path` (E2E uses `scripts/appsettings.e2e.json`). `docker-compose.yml` does **not** set it, so the web container only starts if that file is baked into the image.
- **`MailgunEmailService.SendEmailAsync` returns immediately** (`return;` on line 1) — forgot-password never sends mail.
- **No antiforgery token on JSON POST actions** (`[ValidateAntiForgeryToken]` is commented out in `StudentController.SaveStudent`); admin/account forms do use it.
- **Deleting a student or course cascades its enrollments**; `IArchiveService` is registered but called by nothing.
- **`dotnet run` spawns a child Kestrel process** — kill with `taskkill /T /F` or by port, or the next run hits "address already in use".
