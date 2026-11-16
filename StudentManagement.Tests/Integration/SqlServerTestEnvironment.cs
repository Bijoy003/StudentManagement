namespace StudentManagement.Tests.Integration;

// The SQL Server suite runs against Testcontainers by default. Setting
// SQLSERVER_TEST_CONNECTION points it at an existing server instead (LocalDB, a CI
// service container), which is also how the tests run on machines without Docker.
internal static class SqlServerTestEnvironment
{
    public const string ConnectionStringVariable = "SQLSERVER_TEST_CONNECTION";

    public const string SkipReason =
        "SQL Server integration tests need Docker (Testcontainers) or a " + ConnectionStringVariable +
        " connection string. Start Docker Desktop, or set " + ConnectionStringVariable +
        " (for example 'Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True') and rerun.";

    public static string? ConnectionStringOverride { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value
            ? value
            : null;

    public static bool IsAvailable => ConnectionStringOverride is not null || DockerEnvironment.IsAvailable;
}
