---
name: tdd-feature
description: Use when implementing a new feature or bug fix in the StudentManagement solution following Test-Driven Development (RED-GREEN-REFACTOR). Trigger on "write tests first", "TDD", "implement feature", or when a task has acceptance criteria and you want tests + implementation.
---

# TDD Feature Workflow (StudentManagement)

Use this when adding a feature/fix with tests. It mirrors the solution's multi-agent pipeline roles (`02-test-planner.md`, `03-implementer.md`) but as a single guided workflow.

## Preread

- `AGENTS.md` (root) — file map, conventions, architecture rules, DI scopes, config.
- Existing tests in `StudentManagement.Tests/` for style: `StudentServiceTests.cs`, `CourseRepositoryTests.cs`, `PrimeNumberServiceTests.cs`, `ChatServiceTests.cs`, `AppKnowledgeServiceTests.cs`.

## RED phase — write tests first

1. Define acceptance criteria as individual testable assertions.
2. Map each AC to a test in the matching file (see test-planner agent for placement table and conventions).
3. Test naming: `{Method}_{Scenario}_Returns{Expected}`.
4. Use Moq for service tests, EF Core SQLite in-memory for repository tests.
5. Run `dotnet test --no-restore` — the new tests **must fail** (RED). If they pass, the test doesn't target the unimplemented feature; fix it.

## GREEN phase — implement

1. Build bottom-up: Domain → Application → Infrastructure → Web (see implementer agent / AGENTS.md).
2. Respect reference rules (Domain→nothing, Application→Domain only, Infrastructure→Application, Web→Application+Infrastructure, Tests→Application+Web).
3. Follow conventions: file-scoped namespaces, `_` prefix injected fields, `Async` suffix, constructor injection, no XML docs.
4. If schema changes: entity + DbSet → `dotnet ef migrations add <Name>` (see add-migration skill).
5. Run `dotnet build` then `dotnet test`. All RED tests must go GREEN.

## REFACTOR phase

1. Clean up: no dead code, no duplicate registrations, correct DI scope.
2. Re-run `dotnet test` to confirm still green.

## Deliverables

Report: list of tests (name → AC), list of source files created/modified, and final build + test output. Never modify the known flaky concurrency test.
