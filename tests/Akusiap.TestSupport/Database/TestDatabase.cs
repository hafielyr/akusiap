using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Akusiap.TestSupport.Database;

/// <summary>
/// A uniquely named database on the test server, created empty and dropped on dispose (D-15).
/// The server comes from <c>ConnectionStrings:Test</c>, falling back to <c>ConnectionStrings:Default</c> (D-19), read from
/// the Web project's user-secrets or from environment variables (CI: <c>ConnectionStrings__Test</c>).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string NamePrefix = "akusiap_test_";

    private readonly string _serverConnectionString;

    private TestDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        // Pooled: opening a new TLS connection to a hosted server costs hundreds of milliseconds per query.
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>Creates an empty database.</summary>
    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase(ReadServerConnectionString(), NamePrefix + Guid.NewGuid().ToString("N"));
        await database.ExecuteOnServerAsync($"CREATE DATABASE \"{database.Name}\"");
        return database;
    }

    /// <summary>
    /// Creates the database and applies all migrations. Drops it again if that fails, because xUnit skips
    /// DisposeAsync when InitializeAsync throws.
    /// </summary>
    public static async Task<TestDatabase> CreateMigratedAsync()
    {
        var database = await CreateAsync();
        try
        {
            await using var db = database.CreateContext();
            await db.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);

    public async ValueTask DisposeAsync()
    {
        // Close this database's idle pooled connections first; FORCE ends any that are still busy.
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
    }

    private static string ReadServerConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var value = configuration.GetConnectionString("Test");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = configuration.GetConnectionString("Default");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "Database tests need a PostgreSQL server. Set ConnectionStrings:Test with " +
                "dotnet user-secrets set \"ConnectionStrings:Test\" \"<connection string>\" --project src/Akusiap.Web, " +
                "or the environment variable ConnectionStrings__Test.")
            : value;
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
