---
name: add-migration
description: Use when creating or applying an EF Core migration for the StudentManagement solution, when schema/entity/DbContext changes occur, or when asked to "add a migration", "migrate the database", or "update the database schema". Covers adding a DbContext change, generating the migration, and startup auto-apply behavior.
---

# Add an EF Core Migration

The StudentManagement solution auto-applies migrations at startup (`Program.cs` runs `MigrateAsync()` with 10 retries / 5s delay), so after generating a migration you only need to restart the app — no manual `dotnet ef database update` required.

## When to use

- You added/modified an entity in `StudentManagement.Domain/Entities/`
- You added/modified a `DbSet<T>` in `StudentManagement.Infrastructure/Data/ApplicationDbContext.cs`
- You changed relationships, FKs, indexes, or column constraints

## Steps

1. **Make the model change** first (entity + DbSet). Verify with `dotnet build`.

2. **Generate the migration** from the Infrastructure project:
   ```powershell
   dotnet ef migrations add <Name> --project StudentManagement.Infrastructure --startup-project StudentManagement
   ```
   Use a descriptive PascalCase name, e.g. `AddStudentAuditFields`. The startup project (`StudentManagement`) provides the runtime config (`ConnectionStrings`, etc.); the migrations land in `StudentManagement.Infrastructure/Migrations/`.

3. **Review the generated files** in `StudentManagement.Infrastructure/Migrations/`:
   - `<timestamp>_<Name>.cs` — verify Up/Down are correct
   - `<timestamp>_<Name>.Designer.cs` and `ApplicationDbContextModelSnapshot.cs` — should update automatically
   - Namespace must be `StudentManagement.Migrations`

4. **Note the migration version** for the report (e.g. `20261201103000_AddStudentAuditFields`).

## Important gotchas

- **Don't run `dotnet ef database update`** — startup applies migrations. Just rebuild and run the app.
- The 4 existing migrations are: `20231216072534_AddStudentToDatabase`, `20250817125127_AddCoursesAndEnrollments`, `20250817152747_AddStudentCourseRelationships` (no-op), `20251108103650_CreateIdentityTables`.
- If `dotnet ef` isn't installed, run `dotnet tool install --global dotnet-ef` (version 8.x).
- SQLite in-memory tests use `EnsureCreated()`, not migrations — schema changes do NOT require test-project updates, but new required columns may need seeding in tests.
- For pure seed-data changes (no schema change), modify `DbInitializer.SeedAdmin` in `StudentManagement.Infrastructure/Data/DbInitializer.cs` instead of creating a migration.
