using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;

namespace Akusiap.Web.Jobs;

/// <summary><c>Jobs</c>: background jobs run in-process with Hangfire on PostgreSQL storage (architecture.md, D-67).</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Off in tests, so they never start a Hangfire server or create its schema.</summary>
    public bool Enabled { get; set; } = true;
}

public static class BackgroundJobs
{
    public const string DashboardPath = "/admin/jobs";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>()?.Enabled ?? true;

    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Payments.ReceiptJob>();
        if (!IsEnabled(configuration))
        {
            services.AddScoped<Akusiap.Application.Abstractions.IPaymentJobs, Payments.InlinePaymentJobs>();
            return services;
        }

        services.AddScoped<Akusiap.Application.Abstractions.IPaymentJobs, Payments.HangfirePaymentJobs>();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for background jobs.");
        services.AddHangfire(config => config
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer(options => options.WorkerCount = 2);
        return services;
    }

    /// <summary>Registers the recurring jobs and the admin-only dashboard at <see cref="DashboardPath"/>.</summary>
    public static WebApplication UseBackgroundJobs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!IsEnabled(app.Configuration))
        {
            return app;
        }

        app.UseHangfireDashboard(DashboardPath, new DashboardOptions
        {
            Authorization = [new AdminOnlyDashboard()],
            DisplayStorageConnectionString = false,
        });
        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
        jobs.AddOrUpdate<SweepExpiredAttemptsJob>(SweepExpiredAttemptsJob.Id, job => job.RunAsync(CancellationToken.None), SweepExpiredAttemptsJob.Cron);
        return app;
    }

    private sealed class AdminOnlyDashboard : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) =>
            context.GetHttpContext().User.IsInRole(RoleNames.Admin);
    }
}
