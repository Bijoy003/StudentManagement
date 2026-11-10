# Anti-Patterns

Branch: `feature/software-design-pattern` (WP8 + review rework). Two tiers:

- **(i) FIXED** — anti-patterns removed in this work package, with before/after evidence.
- **(ii) CATALOGUED-ONLY** — real, known, and deliberately left in place (out of WP scope
  or protected by repo rules), documented so they are recognized, not rediscovered.

Companion document: `docs/behavioral-patterns.md`.

---

## (i) FIXED in this WP

### 1. Dead-stub service / unreachable code (`MailgunEmailService`)

**Anti-pattern.** *Stubborn stub / dead code.* `SendEmailAsync` began with a bare
`return;`, making the entire ~40-line Mailgun HTTP call below it unreachable (compiler
warning **CS0162**). The class was DI-registered as a *working* `IMailgunEmailService`, so
every consumer (forgot-password, MFA email) silently did nothing while looking wired up —
the worst kind of stub: one that lies about its contract.

**Before** (`StudentManagement.Application/Services/MailgunEmailService.cs`):

```csharp
public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = false)
{
    return;                    // CS0162: everything below is unreachable
    var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.mailgun.net/v3/{_domain}/messages");
    ...
}
```

**After** — an honest, configuration-gated implementation: the send path is real code,
and skipping is an explicit, logged decision driven by `Mailgun:Enabled`
(default `false` in the committed `StudentManagement/appsettings.json`; real keys stay in
user-secrets/env per AGENTS rule 7):

```csharp
_enabled = bool.TryParse(config["Mailgun:Enabled"], out var enabled) && enabled;

public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = false)
{
    if (!_enabled)
    {
        _logger.LogInformation(
            "Mailgun sending is disabled (Mailgun:Enabled); skipped email with subject {Subject}", subject);
        return;
    }
    ... // real Mailgun HTTP call, now reachable and executed when enabled
}
```

Tests: `StudentManagement.Tests/MailgunEmailServiceTests.cs` (disabled → no HTTP attempt;
enabled → request built and sent).

### 2. Security check commented out (`[ValidateAntiForgeryToken]` on JSON POST)

**Anti-pattern.** *Commented-out safety code.* The CSRF protection on
`StudentController.SaveStudent` was disabled by commenting the attribute out — the code
still "documents" the intent while providing none of it, and reviewers stop seeing it as
a gap because it looks temporary.

**Before** (`StudentManagement/Controllers/StudentController.cs`):

```csharp
[HttpPost]
//[ValidateAntiForgeryToken]
public async Task<IActionResult> SaveStudent([FromBody] Student student)
```

**After** — attribute re-enabled and the client updated to satisfy it:
`StudentManagement/wwwroot/js/student.js` reads the layout-rendered
`__RequestVerificationToken` hidden input and sends it on every JSON POST via the standard
`RequestVerificationToken` header:

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SaveStudent([FromBody] Student student)
```

```js
headers: { 'RequestVerificationToken': getAntiForgeryToken() },
```

Tests: `StudentManagement.Tests/AntiForgeryAttributeTests.cs` (attribute present; HTTP
verb/binding unchanged; Account-controller coverage not regressed).
Residual: `CourseController.Save` / `EnrollmentController.Save` JSON POSTs remain
unguarded — catalogued in the reviewer-visible gap list, out of this WP's scope; the JS
token source is a deferred follow-up (`docs/behavioral-patterns.md`).

### 3. Magic strings for cache keys

**Anti-pattern.** *Magic strings / scattered literals.* Cache keys like `"courses_all"`,
`$"course_{id}"`, `"course_student_counts"` were duplicated as raw literals across
`CourseService`, `StudentService`, `EnrollmentService`, `TeacherService`. Cross-service
eviction only works when two services type the *same* literal; one typo silently strands a
stale entry, and no compiler or test can catch it.

**Before** (`StudentManagement.Application/Services/CourseService.cs` et al.):

```csharp
const string cacheKey = "courses_all";
...
await RemoveAsync("courses_all");
await RemoveAsync("course_student_counts");
await RemoveAsync($"course_{courseId.Value}");
```

**After** — single source of truth `StudentManagement.Application/CacheKeys.cs`
(constants byte-identical to the old literals, so cached data survives the change):

```csharp
public const string AllCourses = "courses_all";
public const string CourseStudentCounts = "course_student_counts";
public static string Course(int courseId) => $"course_{courseId}";
```

```csharp
const string cacheKey = CacheKeys.AllCourses;
...
await RemoveAsync(CacheKeys.AllCourses);
await RemoveAsync(CacheKeys.CourseStudentCounts);
await RemoveAsync(CacheKeys.Course(courseId.Value));
```

Tests: `StudentManagement.Tests/CacheKeysTests.cs` — literal-duplication guard: every
key a service reads/writes/evicts must equal the `CacheKeys` value.

---

## (ii) CATALOGUED-ONLY (known, deliberately not fixed here)

### 4. God object — `StudentManagement/Program.cs`

~326 lines of top-level composition root doing everything: additional-config loading,
Serilog bootstrap, OpenTelemetry/Prometheus, Identity + Google + MFA, rate limiting,
caching, all DI registrations (services, domain events, strategies, validation chain,
chat/RAG, evaluation), middleware pipeline, DB migration/seed, and the `evaluate` CLI
dispatch. It is *the* place every feature touches, so every PR conflicts there and no
piece is unit-testable in isolation. Not fixed: splitting a composition root is a
repo-wide refactor with high blast radius and no behavior payoff; the WP only added
registrations to it. Suggested decomposition when prioritized: extension methods per
concern (`AddObservability`, `AddIdentityWithMfa`, `AddAiChat`, …) mirroring the existing
`AddDomainEvents` / `AddAppChatClient` / `AddEvaluationServices` pattern.

### 5. Commented-out code blocks (dead MFA-email path and friends)

Kept examples: `StudentManagement/Controllers/AccountController.cs` lines ~45
(`//var mfaResult = await _signInManager.TwoFactorSignInAsync(…)`), ~77–84 (email-MFA
code generation + `//await _emailService.SendMfaCodeHtmlAsync(…)`), ~323 (recovery-code
generation); `StudentManagement/Program.cs` lines ~55 (`//builder.Configuration…`) and
~291 (`//app.UseStaticFiles(new StaticFileOptions…`). Version control is the archive —
these blocks mislead readers about what the login/MFA flow actually does (email MFA is
*not* active). Not fixed here: they sit in authentication flows this WP was not chartered
to touch, and deleting them changes no behavior but needs an identity-focused review pass.

### 6. Anemic domain model — entities as property bags (grade-range invariant)

`StudentManagement.Domain/Entities/` holds pure data classes with public setters and no
behavior: `Enrollment.Grade` is a bare `decimal?` with **no 0–100 invariant on the
entity** — out-of-range values are only detected at *display* time, where each grading
strategy returns `"Invalid"` (`IGradingStrategy.ComputeGrade`), and nothing prevents
persisting `Grade = 500`. Likewise student email presence/format lives in the Application
validation chain, and course credit ranges are unenforced. Trade-off accepted for now:
EF Core binding, DTO mapping (`CourseMappingExtensions`, student DTOs) and the MVC/API
surfaces all rely on settable POCOs, so moving invariants into the entities (private
setters, guard clauses, factory methods) is a cross-layer refactor. Discussion anchor:
a real fix means `Enrollment.SetGrade(decimal)` throwing on out-of-range, strategies
consuming the guaranteed-valid value, and a migration-neutral schema (validation is
behavior, not columns).

### 7. Tolerated flaky test — `CourseRepositoryTests.UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition`

The test fires a concurrent update loop against SQLite to *demonstrate* a lost-update
race in `CourseRepository.UpdateCourse`. It is flaky **by design**: it asserts on the
outcome of genuinely concurrent writes, while SQLite serializes writers in-process with
connection/command lifetimes crossing threads — timing shifts flip it between
"race exposed" and infrastructure errors (e.g. `NullReferenceException` inside
`Microsoft.Data.Sqlite.SqliteConnection.RemoveCommand`). Policy: **never modify it** —
AGENTS.md rule 6 forbids touching it; on failure retry once, note it, and proceed (the
rest of the suite must be green). It stays as executable documentation of the race
until a real concurrency strategy (row versioning / optimistic concurrency token on
`Course`) is implemented and the test is retired by its author's intent, not by edit.

---

## Residual-risk summary

| # | Anti-pattern | Status | Risk if ignored |
|---|---|---|---|
| 1 | Mailgun dead stub (CS0162) | FIXED | — (now config-gated + tested) |
| 2 | Commented-out antiforgery | FIXED (SaveStudent) | Course/Enrollment JSON POSTs still CSRF-exposed (cookie-auth, JSON-only, no CORS) |
| 3 | Magic cache-key strings | FIXED | — (guarded by CacheKeysTests) |
| 4 | Program.cs god object | CATALOGUED | merge conflicts, untestable startup |
| 5 | Commented-out code blocks | CATALOGUED | misleading auth/MFA documentation-in-code |
| 6 | Anemic domain model | CATALOGUED | invalid grades/emails persistable |
| 7 | Flaky race test | TOLERATED (AGENTS rule 6) | occasional red CI requiring the retry-once policy |
