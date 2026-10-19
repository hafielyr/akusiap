using Akusiap.Infrastructure.Persistence;
using Xunit;

namespace Akusiap.TestSupport.Database;

/// <summary>
/// One migrated database per test run (D-15), shared through an xUnit collection fixture.
/// Tests sharing it must only rely on rows they created themselves (use <see cref="Builders.TestData"/>, which makes
/// every name unique). Tests that need an empty database or change whole tables use <see cref="TestDatabase"/> directly.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private TestDatabase? _database;

    public string ConnectionString => Database.ConnectionString;

    private TestDatabase Database => _database ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    public AppDbContext CreateContext() => Database.CreateContext();
}
