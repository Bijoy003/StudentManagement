# StudentManagement

ASP.NET Core MVC student management system with AI chat, RAG, and multi-agent DevOps tooling. Clean Architecture, .NET 8, SQL Server.

## Quick Reference

- **Solution:** `StudentManagement.sln` — 6 projects
- **Stack:** .NET 8, EF Core 8 (SQL Server), ASP.NET Core Identity + Google auth, xUnit + Moq
- **AI:** Ollama (default `qwen2.5:3b-instruct`), Microsoft.Extensions.AI, ChromaDB RAG, MCP stdio servers
- **Build:** `dotnet build` | **Unit tests:** `dotnet test` | **E2E:** `scripts/e2e.ps1`
- **CI:** `.github/workflows/dotnet.yml` (build + unit tests on push/PR to `develop`; separate `e2e` job runs Playwright)

## Project Structure (Full File Map)

```
StudentManagement.Domain/                        # Zero NuGet deps, no project refs
  Entities/Student.cs                            # Id, Name(50), Email(100), Phone(20), Address(200), DateOfEnroll?, Enrollments nav
  Entities/Course.cs                             # Id, Name, Credits (no nav back)
  Entities/Enrollment.cs                         # Id, StudentId, CourseId, Grade?; navs Student, Course
  DTOs/CourseStudentCountDto.cs                  # CourseName, StudentCount, course
  Interfaces/IStudentRepository.cs               # GetStudentsAsync, GetStudentByIdAsync, AddAsync, UpdateAsync, DeleteAsync, GetStudentsEnrolledInMoreThan
  Interfaces/ICourseRepository.cs                # GetAllCoursesAsync, GetByIdAsync, AddAsync, UpdateAsync, DeleteAsync, GetStudentCountPerCourseAsync
  Interfaces/IEnrollmentRepository.cs            # GetEnrollmentsAsync, AddAsync, DeleteAsync, GetStudentsInCourseAsync, GetCoursesForStudentAsync

StudentManagement.Application/                   # References Domain only
  Interfaces/IStudentService.cs, ICourseService.cs, IEnrollmentService.cs, IPrimeNumberService.cs
  Interfaces/IChatService.cs                     # GetReplyAsync(IReadOnlyList<ChatMessage>, CancellationToken)
  Interfaces/IAppKnowledgeService.cs             # InitializeAsync(), GetDocumentation(), SearchRelevantChunks(query, maxChunks)
  Interfaces/IMailgunEmailService.cs, IMcpToolProvider.cs
  Services/StudentService.cs, CourseService.cs, EnrollmentService.cs, PrimeNumberService.cs
  Services/ChatService.cs                        # Builds system prompt (RAG + docs + tools), calls IChatClient
  Services/ChatTools.cs                          # 9 [Description] AI tools -> JSON via services
  Services/AppKnowledgeService.cs                # Loads embedded docs, parses, chunks, embeds, ChromaDB upsert + RAG search (dist < 0.7)
  Services/SemanticChunker.cs                    # Paragraph split, sliding-window embedding shift detection (<0.55 = topic shift)
  Services/IDocumentParser.cs, TxtDocumentParser.cs, DocxDocumentParser.cs, PdfDocumentParser.cs, ExcelDocumentParser.cs
  Services/KnowledgeChunk.cs                     # sealed record KnowledgeChunk(string Title, string Content)
  Configuration/ChatFeatureOptions.cs            # Chat:Features
  Configuration/ChromaOptions.cs                 # Chroma section
  Configuration/McpOptions.cs                    # Chat:Mcp section
  AppKnowledge/*.md/.txt/.docx/.pdf              # Embedded resources (RAG source docs)

StudentManagement.Infrastructure/                # References Application (transitively Domain)
  Data/ApplicationDbContext.cs                   # IdentityDbContext<ApplicationUser>; DbSets: Students, Courses, Enrollments
  Data/DbInitializer.cs                          # SeedAdmin: 4 roles + 2 admin users (admin@admin.com / Admin123)
  Repositories/StudentRepository.cs, CourseRepository.cs, EnrollmentRepository.cs
  Migrations/                                    # 4 migrations (see below)

StudentManagement/                               # Web (MVC), references Application + Infrastructure
  Program.cs                                     # DI, rate limiting, Identity, migration+seed retry (10x, 5s)
  Configuration/ChatClientServiceCollectionExtensions.cs   # AddAppChatClient — AI/chat singleton chain
  Configuration/ChatOptions.cs                   # Chat section
  Configuration/McpToolProvider.cs               # Stdio MCP servers via ModelContextProtocol package
  Controllers/HomeController.cs, StudentController.cs, CourseController.cs, EnrollmentController.cs, ChatController.cs, AccountController.cs, AdminController.cs
  Models/                                        # ChatRequest + Identity view models
  Views/{Home,Student,Course,Enrollment,Chat,Account,Admin,Shared}/...
  wwwroot/js/{chat,student,course,enrollment,site}.js
  appsettings.json / .Development.json / .Production.json

StudentManagement.Tests/                         # References Application + Web (unit tests)
  StudentServiceTests.cs, CourseRepositoryTests.cs, PrimeNumberServiceTests.cs, ChatServiceTests.cs, AppKnowledgeServiceTests.cs

e2e/                                             # Node.js Playwright E2E — spec.ts, `npx playwright test`
  package.json                                   # @playwright/test 1.61.0 (reuses chromium-1228 cache)
  playwright.config.ts                           # baseURL :5255, 1 worker, chromium, headless unless HEADED, 30s timeouts
  tsconfig.json                                  # TS config for editor/typecheck
  tests/fixtures.ts                              # Helpers (login/loginAs/logout/unique/create*) + re-exports test/expect
  tests/{auth,student,course,enrollment,chat,admin,account}.spec.ts   # 34 browser tests
scripts/e2e.ps1, scripts/appsettings.e2e.json    # E2E orchestration (DB container + app + npx playwright test)

tickets/Ticket-1852-pipeline-run.md              # Sample per-ticket pipeline run log
01-prd-parser.md ... 05-pr-reviewer.md           # Multi-agent DevOps pipeline role specs
autonomous_devops_pipeline_generic.md            # Pipeline architecture template
StudentMangement/                                # ORPHANED empty folder (note typo) — ignore
```

## Key Conventions

- **.NET 8**, nullable enabled, implicit usings enabled
- **File-scoped namespaces** (`namespace X;`) for new files
- **Constructor injection** with `private readonly` fields prefixed `_`
- **Async suffix** on all async methods
- **Test naming:** `{Method}_{Scenario}_Returns{Expected}`
- **No XML doc comments** on most code; `[Description]` attributes used on AI tool methods (`ChatTools.cs`)
- Web project has `GlobalUsings.cs` (imports Domain.Entities + Application.Interfaces)

## Architecture Rules (project references)

- **Domain** → no NuGet, no project refs
- **Application** → references Domain only
- **Infrastructure** → references Application (transitively Domain)
- **Web** → references Application + Infrastructure (DI wiring)
- **Tests** → references Application + Web
- Never add Domain→Infrastructure/Web or Application→Infrastructure/Web references

## DI Registration

- **Program.cs** (all scoped unless noted): repositories, services, `IPrimeNumberService`, Identity, Google auth, rate limiting, Mailgun `HttpClient`. `IAppKnowledgeService` is **Singleton**. `ChatTools` + `IChatService`/`ChatService` are **Scoped**.
- **ChatClientServiceCollectionExtensions.AddAppChatClient** (all Singleton): OpenAI-compatible `ChatClient` (Ollama `/v1`) with `.AsIChatClient().UseFunctionInvocation().UseLogging()`, `IEmbeddingGenerator` (embedding model), `ChromaClient`/`ChromaConfigurationOptions`, `HttpClient`, `SemanticChunker`, 4 `IDocumentParser`s, `McpToolProvider`.

## Configuration (appsettings.json)

- `ConnectionStrings:DatabaseConnection` — SQL Server
- `IpRateLimiting` — all endpoints 20/s; `post:/Chat/Send` 10/min
- `Mailgun` — ApiKey, Domain, FromEmail
- `Authentication:Google` — ClientId, ClientSecret
- `Chat` — Endpoint (Ollama), Model, ApiKey
- `Chat:Features` — IncludeFullDocumentation, IncludeRag, MaxRagChunks=3, IncludeTools, EmbeddingModel
- `Chat:Mcp` — Enabled + Servers (jira via `uvx mcp-atlassian`; github via npx, disabled). Enabled in Development, disabled in Production.
- `Chroma` — Endpoint `http://localhost:8000`, CollectionName `student-management`
- `AdditionalConfig:Path` — extra required JSON loaded by Program.cs (`C:\inetpub\appsettings.json`)

## Data Layer

- **Migrations:** `20231216072534_AddStudentToDatabase`, `20250817125127_AddCoursesAndEnrollments`, `20250817152747_AddStudentCourseRelationships` (no-op), `20251108103650_CreateIdentityTables` (renamed Students table + Identity tables). Namespace `StudentManagement.Migrations`.
- **Seed:** `DbInitializer.SeedAdmin` creates roles Admin/Teacher/Student/User and admins `admin@admin.com` + `bhugolbijoy003@gmail.com` (password `Admin123`).
- **Startup:** `Program.cs` runs `MigrateAsync()` + `SeedAdmin` with up to 10 retries / 5s delay (handles Docker SQL Server cold start).

## Controllers & Routes

- `StudentController`: Index, GetStudentList (JSON), Create, SaveStudent (POST JSON), DeleteStudent, EnrolledInMoreThan
- `CourseController`: Index, Create, Save (POST), Delete (bool), StudentCountPerCourse
- `EnrollmentController`: Index, Create, Save (POST), Delete (bool), StudentsInCourse, CoursesForStudent
- `ChatController`: Index (GET /Chat), Send (POST /Chat/Send, JSON `ChatRequest`, supports file/image attachments parsed via `IDocumentParser`)
- `AccountController`: Login, MFA (authenticator TOTP + QRCoder), Profile, Password, Google ExternalLogin
- `AdminController`: `[Authorize(Roles = "Admin")]` CreateUser

## AI Chat & RAG Architecture

- **ChatService** → builds prompt from RAG chunks (`AppKnowledgeService.SearchRelevantChunks`, threshold 0.7) + full docs + tools; invokes `IChatClient` with function invocation.
- **ChatTools** → 9 tools (ListStudentsAsync, GetStudentByIdAsync, GetStudentsEnrolledInMoreThanAsync, ListCoursesAsync, GetCourseByIdAsync, GetStudentCountPerCourseAsync, ListEnrollmentsAsync, GetStudentsInCourseAsync, GetCoursesForStudentAsync), each returns JSON via service layer.
- **AppKnowledgeService** → loads embedded `AppKnowledge/**` resources, extracts text via `IDocumentParser`, chunks via `SemanticChunker`, embeds + upserts to ChromaDB collection, queries on demand.
- **SemanticChunker** → splits paragraphs, detects topic shift when sliding-window embedding cosine similarity < 0.55.
- **MCP** → `McpToolProvider` loads stdio server tools prefixed `{serverKey}_`, cached; wired into chat only when `Chat:Mcp:Enabled`.

## Testing

- Framework: xUnit + Moq. Run `dotnet test`. (CI's build job uses `--filter "Category!=E2E"` as a legacy no-op — the E2E suite now lives in `e2e/`.)
- Repository tests use EF Core SQLite in-memory (`Filename=:memory:`, `EnsureCreated`).
- Service tests use Moq on repository interfaces.
- `AppKnowledgeServiceTests` loads embedded resources.
- `CourseRepositoryTests.UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition` is a **pre-existing flaky test** (SQLite concurrency) — do not modify; on CI failure, retry once then flag.
- Test project already references Application + Web (no csproj changes needed).

## E2E Testing (Playwright)

- Node.js Playwright test runner (NOT xUnit/C#). Tests are `spec.ts` files in `e2e/`, run with `npx playwright test` (or `npm test` from `e2e/`). Requires Node.js (20+); the runner resolves via `npx` after `npm install` in `e2e/`.
- **Run:** `scripts/e2e.ps1` — starts a Docker mssql container (`studentmanagement-e2e-db`, port 1433), builds + runs the web app (`Production` env, HTTP `http://localhost:5255`, extra config from `scripts/appsettings.e2e.json`), installs npm deps + Playwright browsers on first run, runs `npx playwright test` (`e2e/playwright.config.ts`), then cleans up (app process tree + container). Add `-KeepDb` to reuse the DB container.
- **Run against a running app:** `cd e2e; npx playwright test` (or `npm test`). For a single spec: `npx playwright test tests/auth.spec.ts`.
- **Watch it live (headed):** `scripts/e2e.ps1 -Headed` opens a real, visible browser window (CI stays headless). CLI equivalent: `npx playwright test --headed`. `$env:BROWSER` (`firefox`/`webkit`) switches engines; `$env:E2E_BASE_URL` overrides the app URL.
- **Test generator (codegen):** `scripts/codegen.ps1` launches the Playwright codegen recorder (`--target playwright`) against the app. It starts the DB container + web app (same infra as `e2e.ps1`), opens a recorder browser where you click/type, and on closing the recorder writes the generated spec to `e2e\tests\codegen.spec.ts` (override with `-Output`), then cleans up. Recorded code is bare Playwright — **adapt it to the suite conventions** (import `{ test, expect }` from `./fixtures`, use the `login`/`unique`/`create*` helpers, keep role/id-based selectors). Pass `-StorageFile auth.json` to save/load login state so recording starts authenticated (log in once in the recorder).
- Tests: 34 browser tests across `{auth,student,course,enrollment,chat,admin,account}.spec.ts`. `tests/fixtures.ts` provides the shared helpers `login`/`loginAs(email, pw)`/`logout`, `unique(label)`, constants `AdminEmail`/`AdminPassword`/`CreatedUserPassword`, and UI helpers `createUserViaAdmin(role)`, `createStudent(name, email)`, `createCourse(name)`, `selectStudentRow(text)`. Base URL comes from env `E2E_BASE_URL` (default `http://localhost:5255`). The suite runs **sequentially** (`workers: 1` in `playwright.config.ts`) against the shared app/DB to avoid the app's IP rate limiter (20/s; `post:/Chat/Send` 10/min) and DB contention. Seeded admin `admin@admin.com` / `Admin123`; users created via the Admin UI use password `Test1234`.
- **Key gotchas:**
  - `Home/Index` renders at the **root URL `/`**, not `/Home/Index` — assert on the `Hello, admin@admin.com` navbar text, not a URL pattern.
  - App startup applies migrations + seeds admin with 10x/5s retry; wait for HTTP 200 before running tests.
  - Killing the app must use `taskkill /T /F` (or kill by port) — `dotnet run` spawns a child Kestrel process that plain `Stop-Process` misses (causes "address already in use" on the next run).
  - **Do not** set the `#date-of-enroll` input to a datetime-local value: it's `type="date"` and rejects it, leaving the field empty; `student.js` `serializeFormData()` then sends the string `"null"` for `DateOfEnroll`, breaking `DateTime?` JSON binding (HTTP 400 "The student field is required."). `student.js` already emits `yyyy-MM-dd` via `.toISOString().slice(0, 10)`.
  - **Enrollment forms are JSON + strict binding:** `enrollment.js` must send `StudentId`/`CourseId`/`Id` as numbers and `Grade` as a number or `null` (not strings) or the server 400s. The controller `EnrollmentController.Save` drops the `Student`/`Course` nav-property ModelState entries — they're never in the payload and their implicit "required" validation would otherwise reject every save.
- **Playwright MCP (global, for opencode):** this machine's global config (`~/.config/opencode/opencode.jsonc`) registers `@playwright/mcp` as two servers — `playwright` (headed, enabled) and `playwright-headless` (disabled), both `--codegen playwright` (generates TS). The agent gets `browser_*` tools to drive the live app against staged changes and generate E2E tests. To switch modes, flip `enabled` for the two servers and restart opencode (config isn't hot-reloaded). The MCP uses its own Playwright 1.63.0-alpha + Chromium v1237 (already installed; distinct from the `e2e/` 1.61 `chromium-1228`). Generated tests must follow the `e2e/` conventions (import `{ test, expect }` from `./fixtures`, use `login`/`unique`/helpers, place specs under `e2e/tests/`).

## Common Tasks

- **Run:** `dotnet run --project StudentManagement`
- **Docker:** `docker-compose up` (web :8080 + mssql 2022 with healthcheck)
- **New migration:** edit schema → `dotnet ef migrations add <Name>` → startup auto-applies
- **New AI tool:** add `[Description]` method in `ChatTools.cs` + add to `GetTools()` list
- **New RAG doc:** drop file in `Application/AppKnowledge/` (embedded automatically)
