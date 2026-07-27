using DotNetEnv;
using Microsoft.Data.SqlClient;

namespace GymLink.IntegrationTests;

internal static class TestSqlServer
{
    public static string ConnectionString(string databaseName)
    {
        Env.TraversePath().NoClobber().Load();
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__GymLink")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__GymLink is required to run SQL Server integration tests.");
        var builder = new SqlConnectionStringBuilder(configuredConnectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }
}
