using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Email;
using Akusiap.Infrastructure.Identity;
using Akusiap.Infrastructure.Persistence;
using Akusiap.Infrastructure.Seeding;
using Akusiap.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
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

        // Users, roles and sign-in (D-31). The Web project adds the cookie scheme and the account pages.
        services.AddIdentityCore<User>(ConfigureIdentity)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DataSeeder>();
        services.AddHostedService<DatabaseInitializer>();

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

    /// <summary>Account rules (D-31): 8+ character passwords without character-class rules, lockout after 5 failures.</summary>
    public static void ConfigureIdentity(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.SignIn.RequireConfirmedEmail = false;
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
