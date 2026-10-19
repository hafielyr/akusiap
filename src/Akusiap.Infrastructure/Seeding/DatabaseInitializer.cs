using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Seeding;

/// <summary>
/// Runs before the app accepts requests: applies migrations when <c>Database:MigrateOnStartup</c> is on, then seeds when
/// <c>Seed:OnStartup</c> is on. Sample content is seeded in Development only.
/// </summary>
internal sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<SeedOptions> seedOptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!databaseOptions.Value.MigrateOnStartup && !seedOptions.Value.OnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        if (databaseOptions.Value.MigrateOnStartup)
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        if (seedOptions.Value.OnStartup)
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>()
                .SeedAsync(includeSampleContent: environment.IsDevelopment(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
