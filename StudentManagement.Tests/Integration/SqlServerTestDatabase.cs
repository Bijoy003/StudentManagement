using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Tests.Integration;

// A throwaway SQL Server database for a single test: created from master,
// brought up to date with the real EF migrations, dropped on dispose.
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private readonly SqlConnectionStringBuilder _admin;
    private readonly SqlConnectionStringBuilder _target;

    private SqlServerTestDatabase(SqlConnectionStringBuilder admin, SqlConnectionStringBuilder target)
    {
        _admin = admin;
        _target = target;

        // EF Core 8 does not validate the model by default, so a stale migration
        // shows up as a failing query instead of a silent pass.
        WriteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(target.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        ReadOnlyOptions = new DbContextOptionsBuilder<ReadOnlyDbContext>()
            .UseSqlServer(target.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;
    }

    public DbContextOptions<ApplicationDbContext> WriteOptions { get; }
    public DbContextOptions<ReadOnlyDbContext> ReadOnlyOptions { get; }

    public string DatabaseName => _target.InitialCatalog;

    public static async Task<SqlServerTestDatabase> CreateAsync(string serverConnectionString)
    {
        var admin = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = "master"
        };
        var target = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = $"StudentManagementTest_{Guid.NewGuid():N}"
        };

        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE [{target.InitialCatalog}];");

        var database = new SqlServerTestDatabase(admin, target);
        await using var context = database.CreateWriteContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public ApplicationDbContext CreateWriteContext() => new(WriteOptions);

    public ReadOnlyDbContext CreateReadOnlyContext() => new(ReadOnlyOptions);

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        try
        {
            await ExecuteAsync(
                _admin.ConnectionString,
                $"ALTER DATABASE [{_target.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" +
                $"DROP DATABASE [{_target.InitialCatalog}];");
        }
        catch (SqlException)
        {
            // Best effort: the container itself is disposed with the collection.
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
