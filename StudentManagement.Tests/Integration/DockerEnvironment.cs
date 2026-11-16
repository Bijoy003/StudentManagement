namespace StudentManagement.Tests.Integration;

// Testcontainers needs a reachable Docker daemon. Probing up front lets the
// SQL Server suite report as Skipped instead of failing on machines without Docker.
internal static class DockerEnvironment
{
    public const string SkipReason =
        "Docker is not available, so the SQL Server integration tests were skipped. " +
        "Start Docker Desktop (or set DOCKER_HOST) and rerun to execute them.";

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        return OperatingSystem.IsWindows()
            ? File.Exists(@"\\.\pipe\docker_engine")
            : File.Exists("/var/run/docker.sock");
    }
}
