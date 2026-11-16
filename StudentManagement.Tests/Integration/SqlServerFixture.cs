using Testcontainers.MsSql;

namespace StudentManagement.Tests.Integration;

// One container per test collection; each test still gets its own freshly
// migrated database on that server (see SqlServerTestDatabase).
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool IsAvailable =>
        SqlServerTestEnvironment.ConnectionStringOverride is not null || _container is not null;

    public string ServerConnectionString =>
        SqlServerTestEnvironment.ConnectionStringOverride
        ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("No SQL Server is available for the integration tests.");

    public async Task InitializeAsync()
    {
        if (SqlServerTestEnvironment.ConnectionStringOverride is not null)
        {
            return;
        }

        if (!DockerEnvironment.IsAvailable)
        {
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
