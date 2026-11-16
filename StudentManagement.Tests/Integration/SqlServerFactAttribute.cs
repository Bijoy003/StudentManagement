namespace StudentManagement.Tests.Integration;

// A [Fact] that reports as Skipped when neither Docker nor a SQL Server connection
// string is available, so a plain `dotnet test` never goes red on a dev box.
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerTestEnvironment.IsAvailable)
        {
            Skip = SqlServerTestEnvironment.SkipReason;
        }
    }
}
