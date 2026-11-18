# StudentManagement

ASP.NET Core 8 MVC for students, courses, and enrollments (Clean Architecture, SQL Server, EF Core 8)
with Identity auth, an AI chat/RAG assistant, a RAG evaluation harness, and Playwright E2E tests.

## Commands

| Task | Command |
|---|---|
| Build / unit tests | `dotnet build` · `dotnet test` |
| Run | `dotnet run --project StudentManagement` (http://localhost:5255) |
| E2E | `scripts/e2e.ps1` (`-Headed`, `-KeepDb`) · or `cd e2e; npx playwright test` |
| Docker stack | `docker-compose up` (web :8080 + SQL Server + Redis) — needs `AdditionalConfig__Path` |
| New migration | `dotnet ef migrations add <Name>` in Infrastructure — never run `database update` by hand |
| RAG evaluation | `dotnet run --project StudentManagement -- evaluate --help` |

Seeded admin: `admin@admin.com` / `Admin123` (and `bhugolbijoy003@gmail.com`). Seeded teacher: `teacher@teacher.com` / `Teacher123`; seeded student: `student@student.com` / `Student123` (Student role; sees My Courses once an admin links it to a student record on Admin/Users).

## Non-negotiable rules

1. **Layering:** Domain (no deps) ← Application; Domain ← Infrastructure; Web → all three; Tests → Application + Web.
   Domain never depends on anything; Application never depends on Infrastructure/Web.
2. **The REST API never binds or serializes domain entities** — use `Application/DTOs/`; no new endpoints that bind entities.
3. **Writes go Application service → repository.** Repositories use `ApplicationDbContext` for writes, `ReadOnlyDbContext` + `AsNoTracking()` for `*ReadOnly`; `Include()` belongs in the repository, not the service.
4. **Style:** file-scoped namespaces, ctor injection with `_camelCase` fields, `Async` suffix, no XML docs (except `[Description]` on `ChatTools`), test names `{Method}_{Scenario}_Returns{Expected}`.
5. **Schema change ⇒ EF Core migration** (startup applies it, 10 retries × 5 s). No raw SQL, no hand-edited history.
6. **Never modify `CourseRepositoryTests.UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition`** — known-flaky SQLite test: retry once, then flag.
7. **No secrets in `appsettings*.json`.** Placeholders in committed config; real values in user-secrets or `Section__Key` env vars.

## Landmines

- **Chat provider is switchable** via `Chat:ActiveProvider` + `Chat:Providers:<name>` (`Endpoint`/`Model`/`ApiKey`); an empty `ActiveProvider` keeps the flat `Chat:Endpoint`/`Model`/`ApiKey` as the default, so existing configs are unaffected. `Chat:EmbeddingProvider` pins embeddings separately (defaults to the active provider) — set it to `Ollama` when chat moves to a provider without an embeddings API. An unknown provider name throws at startup; switching needs a restart. Keys stay in user-secrets (`Chat:Providers:<name>:ApiKey`).
- **`AdditionalConfig:Path` is required** (`optional: false`) — startup throws without it; override with `AdditionalConfig__Path` (E2E uses `scripts/appsettings.e2e.json`). `docker-compose.yml` does **not** set it.
- **Mailgun sends only when `Mailgun:Enabled=true`** (committed default `false`) — forgot-password delivers no mail; test by reading the reset token, not the mailbox.
- **`StudentController.SaveStudent` enforces `[ValidateAntiForgeryToken]`** — `wwwroot/js/student.js` sends it in the `RequestVerificationToken` header. `CourseController.Save` and `EnrollmentController.Save` JSON POSTs still carry no token.
- **Deleting a student or course cascades its enrollments**; `IArchiveService` is registered but called by nothing.
- **`dotnet run` spawns a child Kestrel process** — kill with `taskkill /T /F` or by port, or the next run hits "address already in use".
- `wwwroot/lib/` is gitignored and restored on build; unit tests use SQLite, not SQL Server.

## Where the detail lives

Read the matching file before working in an area; each is also delegable. `*-expert` agents explain/map/audit and do not edit (except `chat-rag-expert`, `rag-evaluation-expert` in their own area); hand code changes to `implementer`.

`.opencode/agent/`: `student-expert` (student slice runbook) · `domain-expert` (entities, invariants, grades, cascade, caching) · `web-api-expert` (routes, status codes, DTO/PATCH, Razor/jQuery) · `architecture-expert` (file map, DI lifetimes, config, migrations, logging) · `identity-security-expert` (login, MFA, Google, reset, roles, security gaps) · `chat-rag-expert` · `rag-evaluation-expert` · `{test-planner,implementer,self-healer,pr-reviewer}` (workflow). Runbooks describe procedure; reference docs are authoritative on rules and win on conflict.

Recipes: `.opencode/skills/*/SKILL.md` — add-migration, ai-chat-tools, rag-knowledge, playwright-e2e, run-and-verify, tdd-feature.

## Default workflow

`test-planner` (RED) → `implementer` (GREEN) → `self-healer` only if build/test fails → `pr-reviewer` before commit.

Prefer `/ship <task>`: the `orchestrator` primary agent drives that chain (intake/ACs → RED → GREEN → heal ≤2 → E2E when UI → review ≤2 cycles) and ends every commit with a `Workflow: RED(n)→GREEN→review PASS` trailer. A commit without the trailer counts as pipeline-skipped — never fake one for stages that didn't run.
