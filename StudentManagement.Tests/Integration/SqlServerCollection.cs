namespace StudentManagement.Tests.Integration;

// SQL Server containers are expensive and port-bound: run this collection serially.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
