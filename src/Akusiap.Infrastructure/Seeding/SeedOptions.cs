namespace Akusiap.Infrastructure.Seeding;

/// <summary>Settings under <c>Seed</c>. Keep the admin password in user-secrets or an environment variable.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Run the seeder when the app starts (default true). Tests turn it off and call the seeder themselves.</summary>
    public bool OnStartup { get; set; } = true;

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }
}

/// <summary>Settings under <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations when the app starts. On in Development only; production migrates during deploy (OPS-02).</summary>
    public bool MigrateOnStartup { get; set; }
}
