using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Services;
using StudentManagement.Application.Validation;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

namespace StudentManagement.Tests.Integration;

// One freshly migrated database plus one write/read-only context pair per test —
// the same lifetime a scoped DI request gives the real repositories.
public abstract class SqlServerIntegrationTest : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    protected SqlServerIntegrationTest(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    protected SqlServerTestDatabase Database { get; private set; } = null!;

    protected ApplicationDbContext WriteContext { get; private set; } = null!;

    protected ReadOnlyDbContext ReadOnlyContext { get; private set; } = null!;

    // Fresh per test, so a cached read can never mask a database round-trip.
    protected IDistributedCache Cache { get; } =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    protected IMemoryCache MemoryCache { get; } =
        new MemoryCache(Options.Create(new MemoryCacheOptions()));

    public async Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        Database = await SqlServerTestDatabase.CreateAsync(_fixture.ServerConnectionString);
        WriteContext = Database.CreateWriteContext();
        ReadOnlyContext = Database.CreateReadOnlyContext();
    }

    public async Task DisposeAsync()
    {
        if (Database is null)
        {
            return;
        }

        await WriteContext.DisposeAsync();
        await ReadOnlyContext.DisposeAsync();
        MemoryCache.Dispose();
        await Database.DisposeAsync();
    }

    protected StudentService CreateStudentService(IValidationHandler? validationChain = null) =>
        new(
            new StudentRepository(WriteContext, ReadOnlyContext),
            new EnrollmentRepository(WriteContext, ReadOnlyContext),
            Cache,
            NullLogger<StudentService>.Instance,
            validationChain);

    protected CourseService CreateCourseService() =>
        new(
            new CourseRepository(WriteContext, ReadOnlyContext),
            Cache,
            NullLogger<CourseService>.Instance);

    protected EnrollmentService CreateEnrollmentService() =>
        new(
            new EnrollmentRepository(WriteContext, ReadOnlyContext),
            MemoryCache,
            Cache,
            NullLogger<EnrollmentService>.Instance);

    protected async Task SeedAsync(Func<ApplicationDbContext, Task> seed)
    {
        await seed(WriteContext);
        await WriteContext.SaveChangesAsync();
    }
}
