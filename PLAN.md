# Implementation Plan — TODO.md remaining items

Companion to `TODO.md`. Work packages (WP) are ordered by dependency and quick wins first.

**Ground rules** (from `AGENTS.md`, apply to every WP):
- Workflow: `test-planner` (RED) → `implementer` (GREEN) → `self-healer` if red → `pr-reviewer` before merge
- One branch per WP off `develop` (`feature/<wp-name>`); schema changes ⇒ EF migration via `add-migration` skill
- Layering: Domain ← Application ← Infrastructure ← Web; REST binds DTOs only; writes go service → repository; reads use `ReadOnlyDbContext` + `AsNoTracking()`
- No secrets in `appsettings*.json` (user-secrets / env vars)

---

## Phase 1 — Database Advanced (finish what develop started)

### WP1. Activate Archive Old Data ⚠️→✅ (effort S, no deps)
The `ArchiveService` works but is never called.
- Add `ArchiveController` (Admin-only, `[Authorize(Roles = "Admin")]`): `POST /Admin/Archive/Run` (enrollments + students, `yearsOld` param), `GET /Admin/Archive/Stats` — returns `ArchiveStats` via DTO, antiforgery token on POST
- Add `ArchiveBackgroundService : BackgroundService` (daily timer, opt-in via config `Archive:Enabled`, `Archive:ScheduleHours`) calling `IArchiveService`
- Tests: controller authz + happy path (mock `IArchiveService`); background service triggers on interval (fake time or short interval)
- Acceptance: admin can archive on demand; scheduled job runs when enabled; stats visible

### WP2. Real Read Replica in docker-compose ⚠️→✅ (effort M, no deps)
- Add `db-replica` service (mssql 2022, own volume + healthcheck); seed via startup migrations (already retries) — keep it a *standalone read-copy*, document that true replication (AG/log-shipping) is infra-level
- Point `ConnectionStrings__DatabaseConnection_ReadReplica` at `db-replica`; add `?ApplicationName=...readonly` so queries are attributable
- Log at startup which server each context targets; add a `/Health` or admin page line showing "reads → replica"
- Tests: `StudentRepository` read methods hit `ReadOnlyDbContext` (existing test style with SQLite); compose config validated by `docker-compose config`
- Stretch: transactional-replication init script in `db/scripts/` so replica data matches primary
- Acceptance: `docker-compose up` → list/read pages served from replica container (visible in its logs), writes still on primary

### WP3. Query Optimization — pagination + search ⚠️→✅ (effort M, no deps)
- `PagedResult<T>` DTO in `Application/DTOs`; extend `I*Repository` read methods with `(int page, int pageSize, string? search, string? sortBy)` using `Skip/Take` on `ReadOnlyDbContext` queries (server-side, `IQueryable` — no in-memory paging)
- Wire into `/api/students` + MVC Student grid (student.js) with page size 20; same for Courses/Enrollments lists
- Tests: repository paging/search/sort on SQLite; API returns correct `totalCount`/page slices
- Acceptance: lists page server-side; search filters by name/email; no full-table loads on list pages

### WP4. Using Right Data Types ⚠️→✅ (effort S, pairs with WP5)
- `OnModelCreating` in `ApplicationDbContext`: `Course.Name` → `HasMaxLength(100)`; `Enrollment.Grade` → `HasPrecision(4,2)` (or `(5,2)`); confirm `Student` attributes already produce `nvarchar(50/100/20/200)`; `datetime2(0)` for `DateOfEnroll`
- Same for `ReadOnlyDbContext` mapping; one migration `TightenColumnTypes` (add-migration skill; never `database update` by hand)
- Tests: snapshot/model test asserting max lengths & precision; migration applies cleanly on SQLite + SQL Server (startup retry proves it)
- Acceptance: no business column is `nvarchar(max)`; grades constrained at DB level

### WP5. Denormalization (effort M, after WP4 — same schema area)
Demonstrate the pattern *and* its consistency cost.
- Candidate: `Enrollment.CourseName` + `Enrollment.StudentName` snapshot columns (list/report queries stop joining); alternative: `Course.EnrollmentCount` maintained transactionally
- Populate on create/update in `EnrollmentService` (write path), backfill in migration for existing rows
- Deliberately document the staleness window; cover with a test that rename-course flow updates (or knowingly doesn't update) snapshots — this becomes the case study
- Tests: enrollment list reads don't `Include` Course/Student; snapshot correctness on create/rename; backfill migration test
- Acceptance: enrollment grid served without joins; trade-offs written up in `docs/denormalization.md`

### WP6. Secure the Database ⚠️→✅ (effort M, pairs with WP12)
- Committed config → placeholders only (`server=localhost`); real values to user-secrets (Web `UserSecretsId` exists) + env vars in compose; scrub `BHUGOLLAP\MSSQLSERVER2019` from `appsettings*.json`
- Least-privilege SQL login for the app: `app_login` with `db_datareader` + `db_datawriter` only (migrations run under a separate admin login at deploy time); script in `db/scripts/create-app-login.sql`, used by compose instead of `sa`
- Drop `TrustServerCertificate=True` in Production config; document cert setup
- Optional stretch: Always-Encrypted / column encryption for `Student.Email`+`Phone` (PII)
- Tests: integration test that app login cannot `CREATE TABLE`/`DROP`; config scan test asserting no `password=`/server names in committed `appsettings*.json`
- Acceptance: app runs without `sa`; committed config has zero environment details

---

## Phase 2 — Software Design (branch `feature/software-design-pattern`, already created)

### WP7. Behavioral Patterns — implement 4, one commit each (effort L, after WP4/WP5 to avoid migration collisions)
1. **Strategy** — `GradeMode` on Course (`LetterGradeStrategy` / `PassFailStrategy` / `GpaStrategy` implementing `IGradingStrategy`); `EnrollmentService` resolves strategy per course to compute/display `Grade`. Schema change ⇒ migration. Tests per strategy + selection test
2. **Observer (domain events)** — lightweight in-process dispatcher (`IDomainEvent`, `IDomainEventHandler<T>`, registered via DI scanning — no MediatR needed): `EnrollmentCreated`/`EnrollmentDeleted` events → handlers invalidate `course_student_counts` cache and (stretch) write an audit row. Replaces the scattered manual cache-invalidation calls. Tests: handler invocation, cache invalidation end-to-end
3. **Template Method** — refactor `TxtDocumentParser`/`DocxDocumentParser`/`PdfDocumentParser`/`ExcelDocumentParser` onto `DocumentParserBase` (`Parse → ExtractText (abstract) → Normalize → Chunk` hooks). Tests: existing parser tests stay green
4. **Chain of Responsibility** — student-creation validation pipeline (`RequiredFieldsRule → EmailFormatRule → DuplicateEmailRule` as `IValidationHandler` chain) used by `StudentService`. Tests: each rule + chain short-circuit
- Deliverable also includes `docs/behavioral-patterns.md`: per pattern — intent, where used, why it fits, UML-ish sketch
- Acceptance: 4 patterns live in production code paths with tests, doc merged

### WP8. Anti-Patterns — audit, fix, document (effort M, independent)
- Catalogue in `docs/anti-patterns.md` with before/after snippets. Known candidates already in repo:
  - **Dead code / unreachable code**: `MailgunEmailService.SendEmailAsync` returns on line 1 (build warning CS0162) → implement properly behind `Mailgun:Enabled` flag or throw `NotImplementedException`
  - **Security anti-pattern**: `[ValidateAntiForgeryToken]` commented out on `StudentController.SaveStudent` → re-enable + fix the JSON POST flow (or switch to token-based API auth and document why)
  - **Commented-out code**: `Program.cs` blocks, `_Layout` leftovers → delete (git remembers)
  - **God object**: top-level `Program.cs` (200+ lines) → extract into `Configuration/*ServiceCollectionExtensions` (partly done already; finish)
  - **Magic strings**: cache keys scattered as literals → central `CacheKeys` constants
  - **Anemic domain model** (discuss, don't force): entities are property bags; note where invariants *should* live (e.g., grade range 0–100 currently unenforced)
  - **Known-flaky test tolerated**: `CourseRepositoryTests.UpdateCourse_ConcurrentLoop…` — document why flaky (SQLite concurrency), don't modify (AGENTS.md rule 6)
- Fix the top 3 (Mailgun stub, antiforgery, magic strings) in the same WP; rest documented
- Acceptance: doc merged, 3 fixes pass review, build has no CS0162

---

## Phase 3 — Operating System (Linux-flavored, exercised through Docker/compose)

### WP9. Shell Scripting — bash parity (effort M, after WP2)
- `scripts/e2e.sh` + `scripts/backup-db.sh` (sqlcmd dump, gzip, retention loop) + `scripts/deploy.sh` — bash versions of the PowerShell tooling so CI/Linux hosts can use them
- Cover the syllabus deliberately: `getopts` arg parsing, `set -euo pipefail`, traps for cleanup, functions, here-docs, cron entry for backup
- CI: GitHub Actions job that shellchecks + smoke-runs them on ubuntu-latest
- Acceptance: `shellcheck` clean; backup script produces a restorable dump inside compose stack

### WP10. User & File Permissions (effort S, pairs with WP6)
- Dockerfile: create non-root `app` user, `USER app`, correct ownership of content root; compose volumes owned by that uid
- `db/scripts/create-app-login.sql` (from WP6) doubles as DB-level "user management"
- `scripts/permissions-demo.sh`: useradd/groupadd/chmod/chown/setfacl/umask walkthrough against a mock deploy dir, with checks (`stat`, `getfacl`) — the study artifact
- Acceptance: web container runs as non-root (`docker inspect` shows uid ≠ 0); demo script runs green

### WP11. Package Management (effort S, with WP10)
- Dockerfile multi-stage hygiene: pinned base image digests, `apt-get install --no-install-recommends` + `rm -rf /var/lib/apt/lists/*`, no `latest` tags; document apt vs snap vs brew vs choco/winget trade-offs in `docs/package-management.md`
- Optional: `winget` manifest or `choco` nuspec for a dev-tool (e.g., sqlcmd) as a hands-on artifact
- Acceptance: image builds deterministic-ish; doc explains pinning + lockfiles (NuGet `packages.lock.json` enabled)

### WP12. Service & System Management (effort M, after WP2)
- `deploy/systemd/studentmanagement.service` (Kestrel behind Kestrel-only or nginx reverse proxy unit file), `WantedBy=multi-user.target`, `Restart=on-failure`, env-file for secrets; README with enable/journalctl workflow
- compose: `restart: unless-stopped` on all services, healthcheck-gated `depends_on` (db/redis already), add `Grafana` service consuming Prometheus metrics
- Windows parity note: `sc.exe create` equivalent for the same app
- Acceptance: unit file validates (`systemd-analyze verify`); Grafana dashboard shows request rate + DB call duration from OpenTelemetry

### WP13. Security & Troubleshooting (OS-level) (effort M, after WP12)
- `deploy/security/`: ufw ruleset script (allow 80/443 only, rate-limit ssh), fail2ban jail config for the app's login endpoint, logrotate config for Serilog rolling files
- `docs/troubleshooting-runbook.md`: symptom → tool flow using what's already wired — Prometheus/Grafana queries, Serilog correlation-ID grep + `jq`, `ss -tulpn`, `docker stats`, `dotnet-trace`/`dotnet-dump` basics, `strace` example
- Add a deliberate incident drill script (`scripts/chaos.sh`: pause redis, kill db connection pool) and the runbook entries that diagnose it
- Acceptance: runbook walks through 3 simulated incidents end-to-end using only documented tools

---

## Suggested order & sizing

| # | WP | Effort | Depends on | Branch |
|---|---|---|---|---|
| 1 | WP1 Archive activation | S | — | feature/activate-archive |
| 2 | WP3 Pagination/search | M | — | feature/query-pagination |
| 3 | WP4 Data types | S | — | feature/column-types |
| 4 | WP2 Read replica | M | — | feature/read-replica-compose |
| 5 | WP6 DB security | M | WP2 (compose) | feature/db-security |
| 6 | WP5 Denormalization | M | WP4 | feature/denormalization |
| 7 | WP7 Behavioral patterns | L | WP4, WP5 | feature/software-design-pattern |
| 8 | WP8 Anti-patterns | M | — | feature/anti-pattern-audit |
| 9 | WP9–WP13 OS bundle | M each | WP2, WP6 | feature/ops-toolkit (one PR per WP) |

Rough total: ~2–3 focused weeks. Phase 1 items are independent — parallelizable. WP7 is the big one; keep each pattern a separate commit/PR-sized chunk.

**Not covered on purpose:** `Miscellaneous Tools and Tips` has no single deliverable — pick up organically (Makefile/justfile in WP12, curl+jq in WP13, devcontainer optional stretch).
