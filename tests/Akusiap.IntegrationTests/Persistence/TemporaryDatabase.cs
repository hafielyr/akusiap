using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>
/// A uniquely named database on the configured server, created empty and dropped on dispose (D-15).
/// Uses ConnectionStrings:Test, falling back to ConnectionStrings:Default (D-19). F-05 replaces this with PostgresFixture.
/// </summary>
internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;

    private TemporaryDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TemporaryDatabase> CreateAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var serverConnectionString = configuration.GetConnectionString("Test") ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set ConnectionStrings:Test or ConnectionStrings:Default to run database tests.");

        var database = new TemporaryDatabase(serverConnectionString, "akusiap_test_" + Guid.NewGuid().ToString("N"));
        await database.ExecuteOnServerAsync($"CREATE DATABASE \"{database.Name}\"");
        return database;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);

    public async ValueTask DisposeAsync() =>
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");

    private async Task ExecuteOnServerAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
