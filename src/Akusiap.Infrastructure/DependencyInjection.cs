using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Application.Questions.Import;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Email;
using Akusiap.Infrastructure.Identity;
using Akusiap.Infrastructure.Importing;
using Akusiap.Infrastructure.Payments;
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

        services.Configure<ImportOptions>(configuration.GetSection(ImportOptions.SectionName));
        services.Configure<PaymentChannelOptions>(configuration.GetSection(PaymentChannelOptions.SectionName));
        services.Configure<Akusiap.Application.Sharing.FeatureOptions>(configuration.GetSection(Akusiap.Application.Sharing.FeatureOptions.SectionName));
        services.AddSingleton<IQuestionFileReader, QuestionFileReader>();
        services.AddSingleton<IQuestionTemplate, QuestionTemplate>();
        services.AddScoped<Jobs.SweepExpiredAttemptsJob>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        // Payments (PAY-04, D-110): DOKU when its keys are set (sandbox unless Doku:Environment is Production, which a
        // Development run refuses); otherwise Development and tests use the fake gateway (PAY-01) and other
        // environments have none (the webhook answers 503 and checkout says payments are unavailable).
        var dokuSection = configuration.GetSection(DokuOptions.SectionName);
        var doku = dokuSection.Get<DokuOptions>() ?? new DokuOptions();
        services.Configure<DokuOptions>(dokuSection);
        services.PostConfigure<DokuOptions>(o =>
        {
            var site = configuration["Site:BaseUrl"];
            if (string.IsNullOrWhiteSpace(o.ReturnUrl) && !string.IsNullOrWhiteSpace(site))
            {
                o.ReturnUrl = site.TrimEnd('/') + "/bayar/{0}";
            }
        });
        if (environment.IsDevelopment())
        {
            services.AddSingleton<FakePaymentGateway>();
        }

        if (doku.IsConfigured)
        {
            if (doku.IsProduction && environment.IsDevelopment())
            {
                throw new InvalidOperationException("Doku:Environment is Production in a Development run. Use DOKU sandbox keys locally.");
            }

            services.AddHttpClient<DokuGateway>(client =>
            {
                client.BaseAddress = new Uri(doku.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<DokuGateway>());
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());
        }

        // Email (D-47, AC-07): a configured SMTP host always sends for real, also in Development (set it with
        // user-secrets). Without one, Development saves .eml files to .dev-mail and other environments drop the
        // email with a warning.
        if (configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Smtp.IsConfigured == true)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailSender>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<EmailOptions>>();
                var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.PickupDirectory));
                return new FileEmailSender(options, directory);
            });
        }
        else
        {
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
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
