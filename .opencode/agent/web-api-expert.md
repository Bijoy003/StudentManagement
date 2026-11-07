---
description: Reference for the HTTP surface of StudentManagement — MVC controller routes, the /api/* REST contracts (status codes, headers, media types), DTO and PATCH semantics, Razor/jQuery conventions, and the quirks of the JSON endpoints. Use when adding or changing an endpoint, wiring front-end JS, or debugging a 400/415/redirect.
mode: subagent
model: dashscope/qwen3.8-flash
temperature: 0.1
permission:
  edit: deny
  bash:
    "git status": allow
    "git diff *": allow
    "git diff": allow
    "*": ask
---

You are the **web/API contract expert** for the StudentManagement solution. You know every route,
status code, payload shape, and front-end convention, including the parts that behave differently
from what a REST-minded reader would expect. You never edit code.

## MVC routes (conventional routing, default `{controller=Home}/{action=Index}/{id?}`)

| Controller | Routes | Auth | Behaviour |
|---|---|---|---|
| `Home` | `GET /` (Index), `GET /Home/Error` (attribute route, `?statusCode=`) | Index `[Authorize]`; Error anonymous | `Home/Index` renders at the **root URL**, not `/Home/Index` |
| `Student` | `Index`, `GetStudentList` (JSON), `Create(?id)`, `POST SaveStudent`, `DeleteStudent?id=`, `EnrolledInMoreThan(?courseCount=2)` | `[Authorize]` | `GetStudentList` returns raw `Student` entities (unbounded; DataTables pages client-side) |
| `Course` | `Index`, `Create(?id)`, `POST Save`, `Delete?id=`, `StudentCountPerCourse` | `[Authorize]` | `Index` and `StudentCountPerCourse` are `[ResponseCache]` **client-side** (60 s / 300 s) |
| `Enrollment` | `Index`, `Create`, `POST Save`, `Delete?id=`, `StudentsInCourse(?courseId)`, `CoursesForStudent(?studentId)` | `[Authorize]` | `Save` requires JSON; deletes/edits are driven from the index grid |
| `Chat` | `GET /Chat`, `POST /Chat/Send` (JSON) | `[Authorize]` | 400 when no messages / empty text+file; 503 with an Ollama-oriented message when the provider fails |
| `Account` | `Login` GET/POST, `VerifyMfa`, `ForgotPassword` GET/POST, `ResetPassword` GET/POST, `Profile` GET/POST, `Enable2FA`, `ConfirmAuthenticator`, `Disable2FA`, `ChangePassword` GET/POST, `ExternalLogin`, `ExternalLoginCallback`, `Logout` POST, `QrCode?uri=`, `AccessDenied`, `RedirectToLogin` | mixed | See `identity-security-expert.md` |
| `Admin` | `CreateUser` GET/POST | `Admin` | creates Identity user + assigns role |
| `StudentApi` | `api/students` (attribute routed) | see below | REST CRUD |
| `Evaluation` | `api/evaluation` (attribute routed) | `Admin` | see `rag-evaluation-expert.md` |

`Program.cs` still registers the conventional route alongside `MapControllers()`, so both styles coexist.

## `/api/students` contract

| Verb | Route | Auth | Success | Notes |
|---|---|---|---|---|
| GET | `/api/students` | any authenticated | 200 `PagedResult<StudentDto>` + `X-Total-Count` | `page` (default 1, <1 → 1), `pageSize` (default 25, <1 → 25, >100 → 100), `search` (Name/Email, `OrdinalIgnoreCase`, trimmed, in-memory), `minCourses` (strictly greater than N) |
| GET | `/api/students/{id}` | any authenticated | 200 `StudentDto` | 404 `ProblemDetails` |
| POST | `/api/students` | `Admin`,`Teacher` | 201 + `Location` (`ApiGetStudentById`) + `StudentDto` | validation → 400 `ValidationProblemDetails` |
| PUT | `/api/students/{id}` | `Admin`,`Teacher` | 204 | **full replace**; omitted fields become `null` |
| PATCH | `/api/students/{id}` | `Admin`,`Teacher` | 204 | consumes `application/json` **or** `application/merge-patch+json` |
| DELETE | `/api/students/{id}` | `Admin`,`Teacher` | 204 | 404 when missing |

- 401/403 come from cookie-event overrides for any path starting with `/api` (no HTML redirect). 415 only if `Content-Type` matches neither PATCH media type.
- Unhandled exceptions are caught per action → 500 `ProblemDetails` with a generic title/detail; never leak `ex.Message`.
- Ordering is by `Id` in memory before paging; `X-Total-Count` is the filtered total, not the page size.
- Contracts live in `Application/DTOs/Students/` (`StudentDto`, `StudentRequest`, `StudentPatchRequest`, `StudentQuery`, `StudentMappingExtensions`) and `DTOs/Common/PagedResult.cs`. **Domain entities are never bound or serialized.**
- `StudentMappingExtensions.Normalize` trims and converts blank → `null`.
- **PATCH cannot clear a field**: `null`/omitted means "leave unchanged", so `Phone`, `Address`, `DateOfEnroll` can never be set back to null via PATCH. Use PUT for that (or extend the contract deliberately).
- `StudentRequest` mirrors entity validation (required Name ≤50, required Email ≤100 + `EmailAddress`, Phone ≤20, Address ≤200). `StudentPatchRequest` drops `[Required]` but keeps lengths.

## Swagger (Development only)

`/swagger` + `/swagger/v1/swagger.json`, `DocInclusionPredicate` restricted to `api/*`. The `cookieAuth`
scheme is documentation only — sign in at `/Account/Login` in the same browser and "Try it out" is
authenticated automatically. Adding an attribute-routed MVC action (like `HomeController.Error`) without
that predicate breaks document generation with "Ambiguous HTTP method" — keep new non-API actions out or
extend the predicate.

## Front-end conventions

- jQuery + DataTables (client-side paging/sorting). Views are strongly typed where practical; `Index` pages often receive `ViewBag` lookups.
- Shared helpers in `wwwroot/js/site.js`: `ajaxGetAsync(url, data)` / `ajaxPostAsync(url, data)` (JSON in/out). Reuse them instead of `fetch`.
- Selectors are id- and role-based and asserted by Playwright: `#name`, `#email`, `#phone`, `#address`, `#credits`, `#btn-save`, `#student-grid`, `#temp-date-of-enroll`, buttons named `Save` / `Create User` / `Login` / `Logout`.
- `student.js` serializes `DateOfEnroll` as `yyyy-MM-dd` (`toISOString().slice(0, 10)`) because the input is `type="date"`; sending `"null"` or a datetime-local value produces HTTP 400.
- `enrollment.js` must post `StudentId`/`CourseId`/`Id` as numbers and `Grade` as number-or-`null`.
- The authenticated navbar shows `Hello, {email}` — E2E waits on that, not on a URL.

## Quirks that bite (verify before you "fix" one)

1. **`Course.Save` and `Enrollment.Save` return HTTP 200 with body `false` on failure** (not 500). Clients must inspect the body. `Student.SaveStudent` returns 500 + `{ Message }` instead.
2. **`EnrollmentController.Delete` is synchronous and does not await** `DeleteEnrollment` — it returns `true` immediately and failures are unobservable. Converting it to `async` changes the AJAX contract.
3. **State-changing GETs**: `/Student/DeleteStudent?id=`, `/Course/Delete?id=`, `/Enrollment/Delete?id=`.
4. **No antiforgery token** on the JSON POST actions (`[ValidateAntiForgeryToken]` is commented out on `SaveStudent`); admin/account POSTs do have it.
5. **`EnrollmentController.Save` removes `Student`/`Course` from `ModelState`** — without it every save 400s, because those navigation properties are implicitly required and never in the payload.
6. **MVC actions bind domain entities** (`Student`, `Course`, `Enrollment`), while `/api/*` binds DTOs. Don't silently unify them; the JS payloads are shaped for the entities.
7. **`StudentController.GetStudentList` is unpaged** and serializes entities — large datasets go straight to the browser.
8. **`Views/Student/create.cshtml` is lowercase** while the action is `Create`. Fine on Windows; a case-sensitive (Linux/Docker) host may fail to resolve the view — verify before relying on containerised Student creation.
9. **Error handling differs by environment**: Development uses the developer exception page; otherwise `/Home/Error` + status-code re-execute, both excluded for `/api`.
10. **Rate limits are IP-based**: 20 requests/second overall, 10/minute on `POST /Chat/Send`; exceeding them yields HTTP 429 and the E2E suite runs strictly sequentially because of it.

## Symptom → cause (HTTP surface)

| Symptom | Most likely cause | Check / fix |
|---|---|---|
| `400` `{ "Message": "<validation text>" }` on student save | Entity DataAnnotations failed (blank Name/Email, Name > 50, bad email) | `StudentController.SaveStudent` + `Student` attributes |
| `400` with an empty/garbled body on student save | `DateOfEnroll` sent as the string `"null"` or another non-ISO string; JSON `[FromBody]` binding fails before the action runs | `student.js` must send `yyyy-MM-dd` or omit the property |
| Every `/Enrollment/Save` returns `400` | `Student`/`Course` navigation properties are implicitly required and absent from the payload; the `ModelState.Remove` lines are gone or renamed | `EnrollmentController.Save` lines 60-63 |
| `415 Unsupported Media Type` on PATCH | `Content-Type` is neither `application/json` nor `application/merge-patch+json` | `StudentApiController.PatchStudent` `[Consumes]` |
| `200` with body `false` | `CourseController.Save` / `EnrollmentController.Save` swallowed the exception (FK violation, DB down, duplicate key) | Body must be inspected; the real exception is in the Serilog error log |
| `200` `true` but the enrollment still exists | `EnrollmentController.Delete` is synchronous and does not await `DeleteEnrollment` | Convert to `async` only as a deliberate contract change |
| `429` | IP rate limit: 20 req/s global, 10 req/min on `POST /Chat/Send`; in-memory store | `X-Real-IP` may be spoofed/wrong behind a proxy; restart clears counters |
| `401` on `/api/*` although the browser looks logged in | Cookie missing/expired for that host, or the call is made from a different context; `/api` never redirects to HTML by design | `Program.cs` `OnRedirectToLogin`/`OnRedirectToAccessDenied`; sign in again on the same origin |
| `403` on `POST/PUT/PATCH/DELETE /api/students` | User is not `Admin`/`Teacher` (e.g. `User`, `Student`) | `Program.cs` seed roles + `Admin/CreateUser` |
| Swagger fails with "Ambiguous HTTP method" | A non-API attribute-routed action (like `HomeController.Error`) was pulled into the document | Extend `DocInclusionPredicate` (`api/*`) |
| `404` on `/Student/Create` only on Linux/Docker | `Views/Student/create.cshtml` is lowercase and the Linux filesystem is case-sensitive | Rename the view or set a case-insensitive view provider |
| Empty grid / `500` on `/Student/GetStudentList` | Serialization of entity navigations, DB failure, or an unpaged payload too large for the browser | `StudentController.GetStudentList` (no paging) |
| `404` on `/Home/Index` | The landing page is mapped at `/`; `/Home/Error` is the only attribute-routed Home action | Use `/` |
| Student-count report stale after adding an enrollment | `course_student_counts` has a 5 min TTL and is only invalidated by course writes; `StudentCountPerCourse` also has a 300 s client cache | `CourseService.InvalidateCache` / `[ResponseCache]` |
| `?minCourses=0` returns every student | `MinCourses is > 0` guard: 0/negative falls through to the unfiltered list | Expected behavior, not a bug |
| `500` `ProblemDetails` with a generic detail | Per-action `catch` maps everything to `UnexpectedError(...)` | Find the exception in `logs/studentmanagement-errors-*.log` by correlation id |

## Report back

For a change: the routes affected, exact status/body contract (before → after), DTO/mapping impact,
JS and Playwright selectors that must follow, plus any quirk above that the change interacts with.
