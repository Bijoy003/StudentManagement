# Caching in StudentManagement — demo runbook

Five caching mechanisms are wired up in the solution. This page is the presenter script: what each
one is, where it lives, how long it lives, and how to make it visible on screen.

## Run the demo

```powershell
pwsh -File scripts/demo-cache.ps1
# Windows PowerShell instead of pwsh:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/demo-cache.ps1
```

The script builds the solution, runs the two unit tests that prove types 2 and 5, starts a disposable
Redis container, starts the app against it, logs in as the seeded admin, walks through all five types,
and then stops the app and removes the container.

| Flag | Purpose |
|---|---|
| `-RedisPort 6380` | Host port for the demo Redis container (default 6380). |
| `-BaseUrl http://localhost:5255` | Where the app listens. |
| `-NoRedis` | Skip Docker; the app falls back to the in-process distributed cache. Types 1's Redis keys/TTLs are then not inspectable, only the HIT/MISS log lines. |
| `-KeepRunning` | Leave the app and the Redis container running after the summary. |
| `-SkipTests` | Do not run the proof tests for types 2 and 5. |

Prerequisites: .NET 8 SDK, SQL Server running (migrations apply at startup), Docker Desktop.
`AdditionalConfig:Path` must resolve (the repo's default `C:\inetpub\appsettings.json` exists on this
machine).

> **Why port 6380?** A Redis inside WSL (or Memurai) often owns `localhost:6379`. If the container
> publishes on 6379 the app silently connects to that other Redis and its keys never appear in
> `docker exec ... redis-cli`. The script uses 6380 and points `ConnectionStrings__Redis` there.

## The five types

| # | Mechanism | Code | Scope / lifetime | How the demo shows it |
|---|---|---|---|---|
| 1 | Distributed cache-aside (`IDistributedCache`) | `StudentService`, `CourseService` | Redis when `ConnectionStrings:Redis` is set, otherwise per-process memory. 60 s lists/single items, 300 s student counts. | Redis keys + TTLs, and `Cache HIT/MISS/INVALIDATE` log lines. |
| 2 | In-process memory cache (`IMemoryCache`) | `AppKnowledgeService.GetDocumentation()`, `EnrollmentService` reads | Process lifetime. Documentation: no expiry (`CacheItemPriority.NeverRemove`) — the embedded knowledge base is immutable per build. Enrollment reads: 60 s absolute, stored as JSON snapshots. | Unit tests (`Assert.Same` for the immutable string; call counts + `Assert.NotSame` for enrollments); log lines `Cache HIT/MISS/INVALIDATE … (in-process)`. |
| 3 | Client HTTP cache (`[ResponseCache]`) | `GET /api/evaluation/dataset` (working), `CourseController` (overridden — see findings) | Browser cache, 300 s. | `Cache-Control: private,max-age=300` response header. |
| 4 | Static asset cache | Single `UseStaticFiles` with `OnPrepareResponse` in `Program.cs` | Browser cache, 1 day for `/css/*`. | `Cache-Control: public,max-age=86400` on `/css/site.css`. |
| 5 | In-process memoization | `McpToolProvider.GetToolsAsync()` | Process lifetime; loaded once behind a `SemaphoreSlim` double-check. | Unit test: 20 concurrent calls trigger exactly one tool load. |

## What the script prints (and what to say)

### 1. Distributed cache — cache-aside

```
=== 1. Distributed cache (cache-aside) - IDistributedCache ===
   Log (1st request)              Cache MISS students_all
   Log (2nd request)              Cache HIT students_all
   Redis key                      StudentManagement:students_all
   Type                           hash
   TTL (seconds)                  59
```

*Read path:* look in Redis → miss → query SQL Server through the read-only `DbContext` → serialize →
write with a 60 s absolute expiry → return.

*Write path:* save to SQL Server, then explicitly remove the affected keys
(`Cache INVALIDATE students_all`, `Cache INVALIDATE student_1002`). Reloading the list logs a fresh
`MISS` and re-populates the cache. This is write-through-invalidate, not write-behind.

Keys used by the app:

| Key (prefix `StudentManagement:`) | TTL | Invalidated by |
|---|---|---|
| `students_all` | 60 s | student save/delete |
| `student_{id}` | 60 s | student save/delete |
| `courses_all` | 60 s | course save/delete |
| `course_{id}` | 60 s | course save/delete |
| `course_student_counts` | 300 s | course save/delete |

`TYPE` is `hash`, not `string`: `StackExchangeRedisCache` stores the payload in the `data` field next
to `absexp`/`sldexp`, so use `HGET <key> data` — `STRLEN` fails with `WRONGTYPE`.

### 2. In-process memory cache

```
Test    AppKnowledgeServiceTests.GetDocumentation_CalledTwice_ReturnsCachedInstance
Result  Passed! - Failed: 0, Passed: 2 ...
[OK] Second call returns the same cached instance instead of re-joining the documents.
```

`GetDocumentation()` joins the embedded knowledge files into one string on every chat message. The
joined string is immutable for the process, so it is cached in `IMemoryCache` without expiry and
`Assert.Same` proves the second call got the cached instance. At runtime (chat with
`Chat:Features:IncludeFullDocumentation=true`) the app log shows the MISS once and HIT afterwards.

`EnrollmentService` uses the same cache for its five read methods with a 60 s absolute TTL. Because
`IMemoryCache` hands back the stored instance, every value is stored as a JSON snapshot and deserialized
per read — a caller that mutates what it received cannot poison the cache for other requests. Keys:

| Key | TTL | Invalidated by |
|---|---|---|
| `enrollments_all` | 60 s | enrollment save/delete |
| `enrollment_{id}` | 60 s | enrollment save/delete (same id) |
| `enrollment_course_{courseId}` | 60 s | enrollment save/delete for that course |
| `enrollment_students_course_{courseId}` | 60 s | enrollment save/delete for that course |
| `enrollment_courses_student_{studentId}` | 60 s | enrollment save/delete for that student |

Enrollment writes also remove the distributed `course_student_counts` key, so the count report no longer
goes stale after an enrollment change. Demo with no Redis and no model: open `/Enrollment` twice
(`MISS` then `HIT … (in-process)`), save an enrollment (`Cache INVALIDATE`), reload (`MISS`). These keys
are process-local, so they cannot be inspected with `redis-cli`.

### 3. Client HTTP cache

```
/api/evaluation/dataset        private,max-age=300
/Course                        no-cache, no-store
```

The evaluation dataset is static for a given build, so `[ResponseCache(Duration = 300,
Location = ResponseCacheLocation.Client)]` is safe there. The browser caches the JSON and does not
re-request it for 5 minutes.

`/Course` is the interesting one — see findings.

### 4. Static assets

```
/css/site.css                  public,max-age=86400
/css/chat.css                  public,max-age=86400
```

One `UseStaticFiles` middleware sets `Cache-Control` for `/css/*` in `OnPrepareResponse`. Everything
else under `wwwroot` is served without that header.

### 5. Memoization

```
Test    McpToolProviderMemoizationTests.GetToolsAsync_ConcurrentCalls_LoadsToolsOnce
Result  Passed! - Failed: 0, Passed: 2 ...
[OK] 20 concurrent calls -> exactly one tool load.
```

MCP is disabled by default (`Chat:Mcp:Enabled=false`), so the live chat path returns no tools. The
memoization pattern is still exercised by the test through an internal loader delegate: 20 concurrent
`GetToolsAsync()` calls, `Interlocked` counter, exactly one load, and every caller gets the same list
instance.

## Findings worth mentioning (honest limitations)

1. **Antiforgery overrides client caching on view pages.** `CourseController` carries
   `[ResponseCache(Duration = 60/300, Location = Client)]`, but every page rendered with `_Layout`
   renders the logout form and its antiforgery token. `DefaultAntiforgery` then forces
   `Cache-Control: no-cache, no-store` and logs a framework warning:
   *"The 'Cache-Control' and 'Pragma' headers have been overridden ... Any response that uses
   antiforgery should not be cached."* That is correct behaviour for per-user HTML containing a
   request token, so the attributes on those two pages are intentional-but-inert. The working example
   is the non-HTML `GET /api/evaluation/dataset`.
2. **Static-file caching was previously dead code.** A bare `app.UseStaticFiles()` ran first and
   short-circuited `/css/*`, so the later path-specific registration never executed. It is now a single
   middleware with a conditional `OnPrepareResponse`.
3. **Enrollment writes now invalidate `course_student_counts`.** Save/delete in `EnrollmentService`
   removes the distributed count key as well as the in-process enrollment keys, closing the window where
   an enrollment change was invisible to the 300 s count report.
4. **Cascade deletes still bypass the enrollment cache.** Deleting a student or course cascades its
   enrollments in the database, but `StudentService`/`CourseService` only clear their own keys, so
   `enrollments_all` and the `enrollment_*` keys can serve deleted rows for up to 60 s; after a student
   delete `course_student_counts` can also be stale for up to 300 s. TTL is the safety net.
5. **Re-pointing an existing enrollment is only partially invalidated.** Saving an enrollment with a
   different `CourseId`/`StudentId` clears the new course/student keys, not the old ones (60 s TTL).
   Not reachable today: no edit endpoint posts a non-zero id.
6. **No Redis configured ⇒ per-process cache.** `AddDistributedMemoryCache()` keeps the app running, but
   HIT/MISS and values diverge between replicas, and “Redis keys” cannot be inspected. The startup log
   line `Distributed cache provider: Redis | in-memory (per-process)` makes the active provider explicit.
   The `EnrollmentService` cache is per-process in every configuration, Redis or not.
7. **No stampede protection on the distributed cache.** Concurrent misses all hit SQL Server; only the
   MCP tool load is serialized (and that is a one-time startup cost).

## Files involved

| File | Role |
|---|---|
| `StudentManagement.Application/Services/StudentService.cs` | Cache-aside for students + HIT/MISS/INVALIDATE logging, awaited invalidation. |
| `StudentManagement.Application/Services/CourseService.cs` | Cache-aside for courses/counts, same logging. |
| `StudentManagement.Application/Services/AppKnowledgeService.cs` | `IMemoryCache` for the immutable documentation string. |
| `StudentManagement.Application/Services/EnrollmentService.cs` | `IMemoryCache` cache-aside (60 s, JSON snapshots) for enrollment reads + invalidation, including the distributed count key. |
| `StudentManagement/Program.cs` | Redis vs in-memory registration, provider log, static-file cache header. |
| `StudentManagement/Controllers/EvaluationController.cs` | Working `[ResponseCache]` example. |
| `StudentManagement/Controllers/CourseController.cs` | Attributes kept with a comment explaining the antiforgery override. |
| `StudentManagement/Configuration/McpToolProvider.cs` | Memoized MCP tool list + test seam. |
| `StudentManagement.Tests/AppKnowledgeServiceTests.cs` | Type 2 proof (`Assert.Same`). |
| `StudentManagement.Tests/EnrollmentServiceTests.cs` | Type 2 proof for enrollment reads: call counts, snapshot isolation (`Assert.NotSame`), invalidation on save/delete. |
| `StudentManagement.Tests/McpToolProviderMemoizationTests.cs` | Type 5 proof (concurrent load count). |
| `scripts/demo-cache.ps1` | The demo itself. |
