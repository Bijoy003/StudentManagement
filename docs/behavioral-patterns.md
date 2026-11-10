# Behavioral Patterns

Branch: `feature/software-design-pattern` (WP7 + WP8). Four behavioral patterns were
introduced test-first (RED → GREEN); every new/changed behavior is pinned by
`StudentManagement.Tests` suites named `{Method}_{Scenario}_Returns{Expected}`.

---

## 1. Strategy — grading per `Course.GradeMode`

**Intent.** Define a family of grade-computation algorithms, encapsulate each one, and
make them interchangeable so a course's grading behavior can vary (Letter / PassFail /
Gpa) without `if/else` ladders in the callers.

**Where it lives.**

| Piece | File |
|---|---|
| Mode enum (`Letter = 0`, `PassFail`, `Gpa`) | `StudentManagement.Domain/Entities/GradeMode.cs` |
| `GradeMode` property on the entity (+ EF migration `AddCourseGradeMode`) | `StudentManagement.Domain/Entities/Course.cs`, `StudentManagement.Infrastructure/Migrations/20261108200425_AddCourseGradeMode.cs` |
| Strategy contract (`Mode`, `ComputeGrade(decimal?)`) | `StudentManagement.Application/Interfaces/IGradingStrategy.cs` |
| Concrete strategies | `StudentManagement.Application/Services/LetterGradeStrategy.cs`, `PassFailStrategy.cs`, `GpaStrategy.cs` |
| Context: resolves the strategy per mode, Letter fallback | `StudentManagement.Application/Services/EnrollmentService.cs` (`GetGradingStrategy`, `ComputeGradeDisplay`) |
| DI: all three registered as `IGradingStrategy` | `StudentManagement/Program.cs` (`AddScoped<IGradingStrategy, …>` × 3) |
| Tests | `StudentManagement.Tests/GradingStrategyTests.cs`, `CourseGradeModeTests.cs`, `CourseApiContractTests.cs` |

**Why it fits.** Grade display is a per-course *algorithm choice*, not per-course state:
the same `decimal?` score maps to `"B"`, `"Pass"` or `"3.0"` depending on the course.
Registering all strategies in DI and selecting by `strategy.Mode == gradeMode` keeps
adding a fourth mode (e.g. weighted) to one new class + one registration — no consumer
changes. Shared edge semantics (`null` → `""`, out-of-range → `"Invalid"`) are part of
the contract, so consumers never special-case.

```
        +---------------------+          resolves by Mode
        |  EnrollmentService  |----------------------------+
        |  (context)          |                            |
        +---------------------+                            v
                                          +--------------------+
                    +---------------------|  IGradingStrategy  |
                    |                     |  + Mode            |
                    |                     |  + ComputeGrade()  |
                    |                     +--------------------+
                    v                              ^    ^
        +-------------------+          +-----------+    +---------+
        | LetterGradeStrategy|         | PassFailStrategy | GpaStrategy |
        | Mode = Letter      |         | Mode = PassFail  | Mode = Gpa  |
        +-------------------+          +------------------+-------------+
```

---

## 2. Observer — in-process domain events (no MediatR)

**Intent.** Decouple "something happened" (an enrollment was created/deleted) from
"what must react" (evicting the distributed course-counts cache) via a publish/subscribe
contract, so new reactions can be added without touching the publisher.

**Where it lives.**

| Piece | File |
|---|---|
| Event marker | `StudentManagement.Domain/Events/IDomainEvent.cs` |
| Handler contract (`IDomainEventHandler<in TEvent>`) | `StudentManagement.Domain/Events/IDomainEventHandler.cs` |
| Events (immutable records) | `StudentManagement.Domain/Events/EnrollmentCreatedEvent.cs`, `EnrollmentDeletedEvent.cs` |
| Dispatcher contract (Domain, dep-free) | `StudentManagement.Domain/Interfaces/IDomainEventDispatcher.cs` |
| Dispatcher implementation (late-bound, sequential) | `StudentManagement.Application/Services/DomainEventDispatcher.cs` |
| Assembly-scanning DI registration (`AddDomainEvents`) | `StudentManagement.Application/DomainEventServiceCollectionExtensions.cs` |
| Concrete observer (cache invalidation) | `StudentManagement.Application/EventHandlers/CourseStudentCountsCacheInvalidationHandler.cs` |
| Publisher (after successful saves/deletes) | `StudentManagement.Application/Services/EnrollmentService.cs` (`SaveEnrollment`, `DeleteEnrollment`) |
| Registration | `StudentManagement/Program.cs` (`AddDomainEvents(typeof(DomainEventDispatcher).Assembly)`) |
| Tests | `StudentManagement.Tests/DomainEventTests.cs` |

**Why it fits.** The stale-course-counts problem was exactly an observer problem: the
manual `RemoveAsync(CacheKeys.CourseStudentCounts)` inside `EnrollmentService` had to be
duplicated anywhere enrollment data changed, and any new side effect would need another
edit of the service. Events keep Domain dependency-free (contracts only), put the
infrastructure-flavored reaction in Application, and register handlers by scanning — a new
handler is one class, zero publisher changes. The dispatcher is deliberately tiny
(sequential, in-process, no outbox): same-request consistency, no new transport.
The optional `IDomainEventDispatcher?` constructor parameter preserves the legacy 4-arg
construction the existing test suites use.

```
  EnrollmentService (publisher)
        |  DispatchAsync(EnrollmentCreatedEvent / EnrollmentDeletedEvent)
        v
  IDomainEventDispatcher  <--impl--  DomainEventDispatcher
        |  resolve IEnumerable<IDomainEventHandler<TEvent>> (runtime type of event)
        v
  +-------------------------------------------+     +--------------------------+
  | CourseStudentCountsCacheInvalidationHandler| ... | (future handlers, scanned)|
  |  evicts CacheKeys.CourseStudentCounts      |     +--------------------------+
  +-------------------------------------------+
```

---

## 3. Template Method — document parser base

**Intent.** Define the skeleton of the text-extraction algorithm once (extract →
normalize → chunk) and defer the format-specific step to subclasses, so all parsers
behave identically except where they must differ.

**Where it lives.**

| Piece | File |
|---|---|
| Abstract template (`ExtractTextAsync` = sealed skeleton; `ExtractText` abstract; `Normalize`/`Chunk` virtual hooks) | `StudentManagement.Application/Services/DocumentParserBase.cs` |
| Public contract, unchanged | `StudentManagement.Application/Services/IDocumentParser.cs` |
| Concrete parsers | `StudentManagement.Application/Services/TxtDocumentParser.cs`, `DocxDocumentParser.cs`, `ExcelDocumentParser.cs`, `PdfDocumentParser.cs` |
| Chunk value type | `StudentManagement.Application/Services/KnowledgeChunk.cs` |
| Tests | `StudentManagement.Tests/DocumentParserBaseTests.cs` (+ existing `AppKnowledgeServiceTests.cs` untouched) |

**Why it fits.** Four parsers had independently grown (or lacked) trim/newline
normalization, and the RAG side will eventually want chunking per format. The template
centralizes the invariant part — every `IDocumentParser` consumer (`AppKnowledgeService`)
now gets normalized text with zero changes to the interface, and a subclass that needs
different chunking overrides one `protected virtual` hook instead of re-deriving the
pipeline.

```
  IDocumentParser                DocumentParserBase (abstract template)
  + CanParse(file)      <--------+ CanParse(file)            [abstract]
  + ExtractTextAsync(s)          + ExtractTextAsync(stream)  [template: 1-2-3-4]
                                 |   1. ExtractText(stream)  [abstract hook]
                                 |   2. Normalize(text)      [virtual, shared default]
                                 |   3. Chunk(text)          [virtual, shared default]
                                 |   4. return normalized text
                                 ^          ^         ^          ^
                              TxtDoc     DocxDoc   ExcelDoc    PdfDoc
```

---

## 4. Chain of Responsibility — student validation

**Intent.** Let a request pass along a chain of validation links, each handling one
concern and forwarding to the next; the first failure short-circuits the chain and the
caller never touches persistence.

**Where it lives.**

| Piece | File |
|---|---|
| Link contract (`SetNext`, `ValidateAsync` → first error or null) | `StudentManagement.Application/Validation/IValidationHandler.cs` |
| Link 1: presence | `StudentManagement.Application/Validation/RequiredFieldsRule.cs` |
| Link 2: email format | `StudentManagement.Application/Validation/EmailFormatRule.cs` |
| Link 3: duplicate email (repository-aware, update-safe) | `StudentManagement.Application/Validation/DuplicateEmailRule.cs` |
| Chain consumer: throws `ValidationException` BEFORE any repository write | `StudentManagement.Application/Services/StudentService.cs` (`SaveStudentAsync`) |
| Chain wiring (order = registration) | `StudentManagement/Program.cs` (`AddScoped<IValidationHandler>(sp => …)` factory) |
| Web contract: `ValidationException` → 400 | `StudentManagement/Controllers/StudentController.cs` (`SaveStudent`, `BadRequest(new { Message })`), `StudentManagement/Controllers/StudentApiController.cs` (`CreateStudent`/`UpdateStudent`/`PatchStudent`, `ValidationProblemDetails`) |
| Tests | `StudentManagement.Tests/StudentValidationChainTests.cs`, `StudentControllerTests.cs`, `StudentApiControllerTests.cs` |

**Why it fits.** Student validation had three concerns of increasing cost (presence —
free; format — cheap; duplicate email — a database read) previously entangled around
ModelState binding. The chain runs cheapest-first, short-circuits on the first failure
(so the DB read never happens for a blank name), is unit-testable link-by-link, and
accepts new rules (e.g. phone format) with one class + one `SetNext`. The optional
`IValidationHandler?` constructor parameter keeps the legacy 4-arg `StudentService`
construction (existing suites) behavior-identical.

```
  StudentService.SaveStudentAsync(student)
        |  ValidateAsync (head)
        v
  +--------------------+   pass   +------------------+   pass   +--------------------+
  | RequiredFieldsRule  |-------->| EmailFormatRule  |--------->| DuplicateEmailRule |--> null (valid)
  | (presence)          |         | (format)         |          | (repo lookup)      |
  +--------------------+         +------------------+          +--------------------+
        | fail                          | fail                          | fail
        v                               v                               v
   first error message (short-circuit — downstream links never run)
        |
        v
  ValidationException  -->  controllers map to 400 (MVC: { Message }; API: ValidationProblemDetails)
```

---

## Deferred follow-ups

Tracked as accepted debt from this WP — none of these blocks merge:

1. **Promote `ComputeGradeDisplay` / `GetGradingStrategy` to `IEnrollmentService`** when a
   real consumer lands (a controller/view/API needing grade display). They are public on
   `EnrollmentService` only; widening the interface now would force mocks in suites that
   don't grade.
2. **`DuplicateEmailRule` is O(n)** — it loads `GetAllAsyncReadOnly()` and scans in memory.
   Replace with a by-email repository query (`GetByEmailAsyncReadOnly`) once the repository
   interface grows one; also removes the case-insensitivity done client-side.
3. **`DocumentParserBase.Chunk` result is currently discarded** — the template invokes the
   hook (so overrides are exercised and pinned by tests) but `ExtractTextAsync` still
   returns the flat string. Wire chunk results into `AppKnowledgeService`/ChromaDB when
   RAG ingestion adopts the base.
4. **`AddDomainEvents` activates handlers lazily** — a handler with unresolvable deps is
   skipped-with-warning at dispatch time (`CreateHandlerOrNull`). Add an eager
   activation check at startup (validate-configuration style) so a mis-wired handler fails
   the host, not a single dispatch.
5. **`Mailgun:Enabled` exists only in `appsettings.json`** (default `false`) — mirror the
   key explicitly into `appsettings.Development.json` / `appsettings.Production.json` when
   environment-specific mail behavior is needed.
6. **Antiforgery token source is the layout's logout-form hidden input**, scraped by
   `wwwroot/js/student.js`. Move to a `<meta>` tag rendered by the layout (or a token
   endpoint) so JSON POSTs don't depend on an unrelated form staying in the DOM.
