---
name: playwright-e2e
description: Use for end-to-end / browser testing of StudentManagement — e.g. "run E2E tests", "run the Playwright suite", "write a browser test", "add a spec", "debug a failing e2e test". Covers running the suite, writing specs, and common gotchas (login, date input, process cleanup).
---

# Playwright E2E (StudentManagement)

Node.js Playwright test runner in `e2e/` with `spec.ts` files (NOT xUnit/C#). Run with `npx playwright test` from `e2e/` (or `npm test`). Requires Node.js 20+; dependencies installed via `npm install` in `e2e/`.

## Run the full suite

```powershell
.\scripts\e2e.ps1
```

What it does: starts Docker mssql container `studentmanagement-e2e-db` (port 1433) → builds + runs the web app (`ASPNETCORE_ENVIRONMENT=Production`, HTTP `http://localhost:5255`, `AdditionalConfig__Path=scripts/appsettings.e2e.json`) → installs npm deps + Playwright browsers on first run → `npx playwright test` (`e2e/playwright.config.ts`) → cleanup.

Useful switches:
- `-KeepDb` — reuse the DB container across runs (faster, keeps seed data).
- `-Headed` — run with a visible browser window so you can watch the tests drive the UI (default is headless, as CI uses).
- `-BaseUrl http://host:port` — point at a running app (still needs the DB container).

## Watching tests live (headed mode)

By default tests run **headless** — the browser is invisible. To watch them:

```powershell
.\scripts\e2e.ps1 -Headed          # full suite in a real browser window
cd e2e
$env:HEADED = '1'
npx playwright test tests/auth.spec.ts   # one spec at a time is easiest to watch
$env:BROWSER = 'firefox'           # switch engine (chromium is default); $env:BROWSER = 'webkit' also works
```

`e2e/playwright.config.ts` reads `HEADED` from the environment when launching the browser; unset it (`$env:HEADED = $null`) or omit `-Headed` to go back to headless. `npx playwright test --headed` also forces headed mode.

## Quick iteration (app already running)

Start the app manually (see `run-and-verify` skill) with the DB container up, then:

```powershell
cd e2e
$env:E2E_BASE_URL = 'http://localhost:5255'
npx playwright test                          # full suite
npx playwright test tests/student.spec.ts    # one spec file
npx playwright test -g "Auth"                # one describe block
```

## Writing specs

- Spec files live in `e2e/tests/*.spec.ts`; each imports `{ test, expect }` from `./fixtures` (which re-exports the Playwright `test`/`expect` and adds app helpers).
- Seeded admin: `admin@admin.com` / `Admin123` (constants `AdminEmail`/`AdminPassword` on `fixtures.ts`).
- `fixtures.ts` exposes `login(page)`/`loginAs(page, email, pw)`/`logout(page)`, `unique(label)`, `BaseUrl`, plus UI helpers `createUserViaAdmin(page, role)`, `createStudent(page, name, email)`, `createCourse(page, name)`, `selectStudentRow(page, text)`. Users created via the Admin UI use password `Test1234`.
- The suite runs **sequentially** (`workers: 1` + `fullyParallel: false` in `playwright.config.ts`) — the app shares one DB and parallel tests would trip the app's IP rate limiter and contend on the DB.
- All specs use relative URLs (e.g. `page.goto('/Student')`) — `baseURL` comes from `E2E_BASE_URL` (default `http://localhost:5255`).

## Critical gotchas

1. **Home/Index renders at the ROOT URL `/`**, not `/Home/Index`. After login, wait for the navbar text `Hello, admin@admin.com` (see `loginAs`), do NOT assert `/Home/Index`.
2. **`#date-of-enroll` is `type="date"`.** Setting it to a datetime-local value (`2026-07-31T11:07`) makes the browser reject it → field stays empty → `student.js serializeFormData()` sends `"DateOfEnroll":"null"` → `DateTime?` JSON binding fails → HTTP 400 "The student field is required." If the save silently stays on `/Student/Create`, this is why. `student.js` already sets the correct `yyyy-MM-dd` via `.toISOString().slice(0, 10)`.
3. **Kill the app with the whole process tree** — `dotnet run` spawns a child Kestrel process. Plain `Stop-Process` leaves it bound to the port → next run fails with "address already in use". `e2e.ps1` kills the listener by port first, then the parent, then strays.
4. **Unit vs E2E**: `dotnet test` at the solution root only runs the unit projects — the E2E suite is Node and is NOT part of the .NET solution. Run it only through `scripts/e2e.ps1` or `npx playwright test` in `e2e/`.
5. **Zombie app + deleted DB**: if you re-run without `-KeepDb` while an old app process still holds port 5255, the zombie serves `/Account/Login` (200) but login POSTs fail because its DB was removed. If login tests time out, kill all `dotnet` processes and restart.
6. **Enrollment forms are JSON + strict binding.** `enrollment.js` converts `StudentId`/`CourseId`/`Id` to numbers and `Grade` to a number or `null` before POSTing — send strings and System.Text.Json rejects the payload (400). `EnrollmentController.Save` also drops the `Student`/`Course` nav-property ModelState entries (they're never posted, but their implicit "required" validation would reject every save). If an enrollment test hangs on `/Enrollment/Create`, check the POST body via `page.on('request', ...)` or the trace viewer.
7. **Course name is required.** `Course.cs` has `[Required]` + `[StringLength(100)]`, and `course.js` blocks empty-name saves with a toastr warning (mirrors `student.js`). An empty-name test asserts the page stays on `/Course/Create` — don't expect a 400 or a redirect.

## Debugging a failing test

- `npx playwright test` prints the failing assertion with a code frame and a diff. Test names follow `{Method}_{Scenario}_Returns{Expected}`.
- Trace + HTML report: config enables `trace: 'retain-on-failure'`. View with `npx playwright show-report` (report written to `e2e/playwright-report`).
- To see network traffic, add `page.on('request', r => console.log(r.url(), r.postData()))` temporarily, or open the trace and inspect the request bodies.
- Default Playwright timeout is 30s per action/assertion (set in `playwright.config.ts`); per-assertion overrides are passed as options (e.g. `{ timeout: 10_000 }`).

## CI

GitHub Actions job `e2e` in `.github/workflows/dotnet.yml`: runs after `build` on push/PR to `develop`; sets up Node 20, then executes `./scripts/e2e.ps1 -KeepDb` under `pwsh` on `ubuntu-latest`. The `build` job runs unit tests with `--filter "Category!=E2E"` (legacy no-op).
