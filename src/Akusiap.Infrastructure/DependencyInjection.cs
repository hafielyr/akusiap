using Akusiap.Application.Abstractions;
using Akusiap.Infrastructure.Email;
using Akusiap.Infrastructure.Persistence;
using Akusiap.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    public const string DatabaseHealthCheckName = "database";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var connectionString = GetRequiredConnectionString(configuration);
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>(DatabaseHealthCheckName);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        if (environment.IsDevelopment())
        {
            // Production SMTP sender comes with AC-02.
            services.AddSingleton<IEmailSender>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EmailOptions>>();
                var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.PickupDirectory));
                return new FileEmailSender(options, directory);
            });
        }

        return services;
    }

    /// <summary>Reads <c>ConnectionStrings:Default</c> or fails startup with a message that names the setting.</summary>
    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing setting 'ConnectionStrings:{ConnectionStringName}'. " +
                $"In development run: dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<connection string>\" --project src/Akusiap.Web. " +
                $"Elsewhere set the environment variable ConnectionStrings__{ConnectionStringName}.");
        }

        return value;
    }
}
