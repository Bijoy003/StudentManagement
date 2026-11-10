# TODO

Status legend: ✅ done · ⚠️ partial · ❌ not started
(checked against `feature/new-features` after merging `origin/develop` @ 2cdb2e2)

## Database - Advanced
- [ ] Denormalization — ❌ schema fully normalized, no computed/denormalized columns
- [x] Usage of Caching — ✅ Redis-or-memory `IDistributedCache` cache-aside w/ TTL + HIT/MISS/INVALIDATE logging (Course/Enrollment/Student services), `IMemoryCache`, `[ResponseCache]`, static-asset caching, memoization; demo: `scripts/demo-cache.ps1`
- [ ] Archive Old Data — ⚠️ `ArchiveService` + `StudentsArchive`/`EnrollmentsArchive` tables scaffolded & DI-registered, but nothing calls them yet (no scheduler/endpoint)
- [ ] Use Read Replicas — ⚠️ `ReadOnlyDbContext` + `DatabaseConnection_ReadReplica` wired with fallback to primary; no real replica server yet (compose points to same DB)
- [ ] Using Right Data Types — ⚠️ `decimal(18,2)` for grades only; strings still `nvarchar(max)`, no `HasMaxLength`/`HasPrecision` configured
- [ ] Secure the Database — ⚠️ Windows auth + secrets moved to user-secrets (`UserSecretsId`); still `TrustServerCertificate=True` and a real server name committed in `appsettings.json`
- [ ] Query Optimization Techniques — ⚠️ near-done: all reads via `ReadOnlyDbContext` + `AsNoTracking()`, eager `Include` in repositories, FK indexes, cache offload; missing pagination (`Skip`/`Take`)

## Software Design
- [x] Design Patterns - Behavioral Patterns — ✅ 4 patterns implemented + tested (Strategy=grading, Observer=domain events, Template Method=parser base, Chain of Responsibility=student validation) with `docs/behavioral-patterns.md`; branch `feature/software-design-pattern`
- [x] Anti Patterns — ✅ top-3 fixed (Mailgun dead stub → `Mailgun:Enabled` gate, antiforgery re-enabled on `SaveStudent` JSON POST, magic cache keys → `CacheKeys`); rest catalogued in `docs/anti-patterns.md`

## Operating System
- [ ] Basic/Advanced Shell Scripting — ⚠️ solid PowerShell now in `scripts/` (`e2e.ps1` container lifecycle, `demo-cache.ps1`, `codegen.ps1`); no bash/Linux scripting
- [ ] User and File Permissions Management — ❌ app-level roles/MFA only, nothing OS-level
- [ ] Package Management — ⚠️ NuGet + npm/Playwright automated in scripts & CI; no OS-level (apt/choco/winget) work
- [ ] Miscellaneous Tools and Tips — ⚠️ Docker, EF codegen script, process-cleanup tips in AGENTS.md
- [ ] Service and System Management — ⚠️ docker-compose (web + SQL Server + Redis, healthchecks), 2 GitHub Actions workflows; no systemd/Windows-service management
- [ ] Security and Troubleshooting — ⚠️ strong app-level: Serilog + correlation IDs, OpenTelemetry, secured Prometheus `/metrics`, IP rate limiting; no OS-level hardening/troubleshooting
