using Akusiap.Application.Abstractions;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Akusiap.TestSupport.Web;

/// <summary>
/// The app wired for tests: the given database, a <see cref="TestClock"/> and a <see cref="FakeEmailSender"/>.
/// Create one per test (it is cheap) so clock and inbox are never shared between tests.
/// </summary>
public sealed class AppFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string SupportWhatsAppNumber = "6281200000000";

    public AppFactory(PostgresFixture database)
        : this((database ?? throw new ArgumentNullException(nameof(database))).ConnectionString)
    {
    }

    public TestClock Clock { get; } = new();

    public FakeEmailSender Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Support:WhatsAppNumber", SupportWhatsAppNumber);
        // The per-run database is already migrated, and tests seed what they need (seeder tests call DataSeeder).
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:OnStartup", "false");
        // Never pick up a developer's admin from user-secrets; tests that need one set it explicitly.
        builder.UseSetting("Seed:AdminEmail", string.Empty);
        // Never call DOKU from tests, even when a developer has sandbox keys in user-secrets: use the fake gateway.
        builder.UseSetting("Doku:ClientId", string.Empty);
        builder.UseSetting("Doku:SecretKey", string.Empty);
        // Serve the build's precompressed static files, as production does, instead of the Development handler that
        // re-sends them uncompressed for live reload; the first-load budget (OPS-01) then measures real transfer (D-104).
        builder.UseSetting("ReloadStaticAssetsAtRuntime", "false");
        builder.UseSetting("Seed:AdminPassword", string.Empty);
        // Email links use the test server address unless a test sets Site:BaseUrl.
        builder.UseSetting("Site:BaseUrl", string.Empty);
        // No Hangfire server or schema in tests; jobs are run directly.
        builder.UseSetting("Jobs:Enabled", "false");
        // Google sign-in is off unless a test turns it on (a developer may have real keys in user-secrets).
        builder.UseSetting("Authentication:Google:ClientId", string.Empty);
        builder.UseSetting("Authentication:Google:ClientSecret", string.Empty);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<Akusiap.Infrastructure.Payments.FakePaymentGateway>());
        });
    }
}
